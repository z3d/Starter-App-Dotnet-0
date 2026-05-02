namespace StarterApp.Tests.Consistency;

/// <summary>
/// Computes Mahalanobis distance of cohort members from an exemplar centroid.
///
/// Mahalanobis distance accounts for both scale differences and correlations between
/// features: d(x, mu) = sqrt((x - mu)^T * Sigma^{-1} * (x - mu)), where Sigma is
/// the covariance matrix of the exemplar set.
///
/// <para>
/// <b>Feature normalisation.</b> Before any distance is computed, feature vectors
/// are z-scored against the full cohort (<c>(x - cohort_mean) / cohort_std</c>, per
/// feature). Without this, a large-scale feature (e.g. IL byte size, range ~500-4000)
/// swamps everything else: the Mahalanobis distance collapses to a sort on that one
/// dimension and every other feature contributes &lt;2%. Normalising puts all
/// features on comparable footing so the covariance structure and the other
/// features actually influence the score.
/// </para>
///
/// <para>
/// With small exemplar sets (N &lt; p), the sample covariance matrix is singular and
/// cannot be inverted directly. We use Ledoit-Wolf shrinkage to regularise it:
/// <c>Sigma_reg = (1 - alpha) * S + alpha * F</c>, where <c>S</c> is the sample
/// covariance, <c>F</c> is a scaled identity target (<c>mu_F * I</c> with
/// <c>mu_F</c> = average diagonal of <c>S</c>), and alpha is the optimal shrinkage
/// intensity computed analytically. When alpha approaches 1 (very few exemplars),
/// the result is close to standardised Euclidean; as exemplars grow, alpha shrinks
/// toward 0 and the full covariance structure dominates.
/// </para>
/// </summary>
public static class ConsistencyScorer
{
    public static double[] ComputeCentroid(IReadOnlyList<ICohortFingerprint> exemplars)
    {
        if (exemplars.Count == 0)
            throw new ArgumentException("At least one exemplar is required.", nameof(exemplars));

        var dim = exemplars[0].ToVector().Length;
        var centroid = new double[dim];

        foreach (var exemplar in exemplars)
        {
            var vec = exemplar.ToVector();
            for (var i = 0; i < dim; i++)
                centroid[i] += vec[i];
        }

        for (var i = 0; i < dim; i++)
            centroid[i] /= exemplars.Count;

        return centroid;
    }

    /// <summary>
    /// Computes the regularized inverse covariance matrix using Ledoit-Wolf shrinkage.
    /// Returns a flattened p x p matrix in row-major order.
    /// </summary>
    public static double[] ComputeInverseCovariance(IReadOnlyList<ICohortFingerprint> exemplars, double[] centroid)
    {
        var n = exemplars.Count;
        var p = centroid.Length;

        // Step 1: Sample covariance matrix S (p x p)
        var s = new double[p * p];
        foreach (var exemplar in exemplars)
        {
            var vec = exemplar.ToVector();
            for (var i = 0; i < p; i++)
                for (var j = 0; j < p; j++)
                    s[i * p + j] += (vec[i] - centroid[i]) * (vec[j] - centroid[j]);
        }

        for (var i = 0; i < p * p; i++)
            s[i] /= n;

        // Step 2: Shrinkage target F = mu_F * I (scaled identity)
        var muF = 0.0;
        for (var i = 0; i < p; i++)
        {
            var diag = s[i * p + i];
            muF += diag < 1e-10 ? 1.0 : diag;
        }

        muF /= p;

        // Step 3: Ledoit-Wolf optimal shrinkage intensity
        var alpha = ComputeShrinkageIntensity(exemplars, centroid, s, muF, n, p);

        // Step 4: Regularized covariance = (1 - alpha) * S + alpha * mu_F * I
        var sigmaReg = new double[p * p];
        for (var i = 0; i < p; i++)
        {
            for (var j = 0; j < p; j++)
            {
                sigmaReg[i * p + j] = (1.0 - alpha) * s[i * p + j];
                if (i == j)
                    sigmaReg[i * p + j] += alpha * muF;
            }
        }

        // Step 5: Invert via Cholesky decomposition (regularized matrix is positive definite)
        return InvertSymmetricPositiveDefinite(sigmaReg, p);
    }

    /// <summary>
    /// Computes Mahalanobis distance: sqrt((x - mu)^T * Sigma^{-1} * (x - mu)).
    /// </summary>
    public static double ComputeDistance(ICohortFingerprint fingerprint, double[] centroid, double[] inverseCov)
    {
        var vec = fingerprint.ToVector();
        var p = centroid.Length;
        var diff = new double[p];
        for (var i = 0; i < p; i++)
            diff[i] = vec[i] - centroid[i];

        // d^2 = diff^T * inverseCov * diff
        var sum = 0.0;
        for (var i = 0; i < p; i++)
            for (var j = 0; j < p; j++)
                sum += diff[i] * inverseCov[i * p + j] * diff[j];

        return Math.Sqrt(Math.Max(sum, 0.0));
    }

    public static IReadOnlyList<CohortScore> ScoreAll(
        IReadOnlyList<ICohortFingerprint> allMembers,
        IReadOnlyList<ICohortFingerprint> exemplars)
    {
        // Z-score features against the full cohort before any distance work. Without this,
        // a feature with 100x the variance of its peers dominates the score and the
        // Mahalanobis machinery collapses to a single-feature sort.
        var (normalisedAll, normalisedExemplars) = NormaliseAgainstCohort(allMembers, exemplars);

        var centroid = ComputeCentroid(normalisedExemplars);
        var inverseCov = ComputeInverseCovariance(normalisedExemplars, centroid);
        var featureNames = exemplars[0].FeatureNames;
        var p = centroid.Length;

        return normalisedAll
            .Select((m, i) => new CohortScore(
                allMembers[i].TypeName,
                ComputeDistance(m, centroid, inverseCov),
                allMembers[i],
                ComputeFeatureContributions(m, centroid, inverseCov, featureNames, p)))
            .OrderByDescending(s => s.Distance)
            .ToList();
    }

    private static (IReadOnlyList<ICohortFingerprint> All, IReadOnlyList<ICohortFingerprint> Exemplars)
        NormaliseAgainstCohort(
            IReadOnlyList<ICohortFingerprint> allMembers,
            IReadOnlyList<ICohortFingerprint> exemplars)
    {
        // Normalise against the union of allMembers and exemplars. Using allMembers alone
        // collapses to zero-variance when the caller passes a single candidate
        // (e.g. governance tests that score one handler against a pinned centroid), which
        // would make every feature z-score to 0. The union always has enough spread to
        // define a meaningful scale.
        var reference = allMembers.Concat(exemplars).Distinct().ToList();
        var p = reference[0].ToVector().Length;
        var means = new double[p];
        var stds = new double[p];

        foreach (var m in reference)
        {
            var v = m.ToVector();
            for (var i = 0; i < p; i++)
                means[i] += v[i];
        }
        for (var i = 0; i < p; i++)
            means[i] /= reference.Count;

        foreach (var m in reference)
        {
            var v = m.ToVector();
            for (var i = 0; i < p; i++)
            {
                var d = v[i] - means[i];
                stds[i] += d * d;
            }
        }
        for (var i = 0; i < p; i++)
            stds[i] = Math.Sqrt(stds[i] / reference.Count);

        ICohortFingerprint Zscore(ICohortFingerprint f)
        {
            var v = f.ToVector();
            var z = new double[p];
            for (var i = 0; i < p; i++)
                z[i] = stds[i] > 1e-10 ? (v[i] - means[i]) / stds[i] : 0.0;
            return new NormalisedFingerprint(f.TypeName, z, f.FeatureNames, f.FeatureKinds);
        }

        return (
            allMembers.Select(Zscore).ToList(),
            exemplars.Select(Zscore).ToList()
        );
    }

    private sealed record NormalisedFingerprint(
        string TypeName,
        double[] Vector,
        string[] FeatureNames,
        FeatureKind[] FeatureKinds) : ICohortFingerprint
    {
        public double[] ToVector() => Vector;
    }

    /// <summary>
    /// Per-feature contribution to the Mahalanobis distance. Because the inverse covariance
    /// has off-diagonal terms, contributions are computed as the absolute value of each
    /// feature's contribution to the quadratic form, normalised to sum to 1.
    /// </summary>
    private static IReadOnlyDictionary<string, double> ComputeFeatureContributions(
        ICohortFingerprint fingerprint, double[] centroid, double[] inverseCov, string[] featureNames, int p)
    {
        var vec = fingerprint.ToVector();
        var diff = new double[p];
        for (var i = 0; i < p; i++)
            diff[i] = vec[i] - centroid[i];

        // Feature i's contribution = |diff[i] * sum_j(inverseCov[i,j] * diff[j])|
        var contributions = new Dictionary<string, double>();
        var total = 0.0;
        var perFeature = new double[p];

        for (var i = 0; i < p; i++)
        {
            var rowContrib = 0.0;
            for (var j = 0; j < p; j++)
                rowContrib += inverseCov[i * p + j] * diff[j];
            perFeature[i] = Math.Abs(diff[i] * rowContrib);
            total += perFeature[i];
        }

        for (var i = 0; i < p; i++)
            contributions[featureNames[i]] = total > 0 ? perFeature[i] / total : 0;

        return contributions;
    }

    /// <summary>
    /// Ledoit-Wolf optimal shrinkage intensity (analytical formula).
    /// Reference: Ledoit &amp; Wolf (2004), "A well-conditioned estimator for
    /// large-dimensional covariance matrices".
    /// </summary>
    private static double ComputeShrinkageIntensity(
        IReadOnlyList<ICohortFingerprint> exemplars, double[] centroid,
        double[] sampleCov, double muF, int n, int p)
    {
        // Numerator: sum of squared estimation error of off-diagonals + (diagonal - muF)^2
        // Simplified: uses the asymptotic formula for the optimal shrinkage
        var vectors = exemplars.Select(e => e.ToVector()).ToList();

        // Compute sum of squared sample covariance entries (Frobenius norm squared of S - F)
        var delta = 0.0;
        for (var i = 0; i < p; i++)
            for (var j = 0; j < p; j++)
            {
                var target = i == j ? muF : 0.0;
                var d = sampleCov[i * p + j] - target;
                delta += d * d;
            }

        // Compute sum of squared fourth moments (estimation variance of S entries)
        var beta = 0.0;
        for (var k = 0; k < n; k++)
        {
            var zz = new double[p * p];
            for (var i = 0; i < p; i++)
                for (var j = 0; j < p; j++)
                    zz[i * p + j] = (vectors[k][i] - centroid[i]) * (vectors[k][j] - centroid[j]);

            for (var i = 0; i < p; i++)
                for (var j = 0; j < p; j++)
                {
                    var d = zz[i * p + j] - sampleCov[i * p + j];
                    beta += d * d;
                }
        }

        beta /= (double)(n * n);

        // alpha = beta / delta, clamped to [0, 1]
        if (delta < 1e-20)
            return 1.0; // S ≈ F already, maximum shrinkage

        return Math.Clamp(beta / delta, 0.0, 1.0);
    }

    /// <summary>
    /// Inverts a symmetric positive definite matrix via Cholesky decomposition.
    /// Input/output are flattened p x p row-major arrays.
    /// </summary>
    private static double[] InvertSymmetricPositiveDefinite(double[] matrix, int p)
    {
        // Cholesky: A = L * L^T
        var l = new double[p * p];
        for (var i = 0; i < p; i++)
        {
            for (var j = 0; j <= i; j++)
            {
                var sum = 0.0;
                for (var k = 0; k < j; k++)
                    sum += l[i * p + k] * l[j * p + k];

                if (i == j)
                    l[i * p + j] = Math.Sqrt(Math.Max(matrix[i * p + i] - sum, 1e-20));
                else
                    l[i * p + j] = (matrix[i * p + j] - sum) / l[j * p + j];
            }
        }

        // Invert L (lower triangular)
        var lInv = new double[p * p];
        for (var i = 0; i < p; i++)
        {
            lInv[i * p + i] = 1.0 / l[i * p + i];
            for (var j = i + 1; j < p; j++)
            {
                var sum = 0.0;
                for (var k = i; k < j; k++)
                    sum += l[j * p + k] * lInv[k * p + i];
                lInv[j * p + i] = -sum / l[j * p + j];
            }
        }

        // A^{-1} = L^{-T} * L^{-1}
        var inv = new double[p * p];
        for (var i = 0; i < p; i++)
            for (var j = 0; j <= i; j++)
            {
                var sum = 0.0;
                for (var k = i; k < p; k++)
                    sum += lInv[k * p + i] * lInv[k * p + j];
                inv[i * p + j] = sum;
                inv[j * p + i] = sum;
            }

        return inv;
    }
}

public record CohortScore(
    string TypeName,
    double Distance,
    ICohortFingerprint Fingerprint,
    IReadOnlyDictionary<string, double> FeatureContributions)
{
    public string TopContributor =>
        FeatureContributions.MaxBy(kv => kv.Value).Key;
}
