using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Infrastructure.Payloads;

public class AzureBlobAppendTests
{
    private readonly Mock<BlobContainerClient> _container = new();
    private readonly Mock<AppendBlobClient> _blob = new();
    private readonly AzureBlobPayloadArchiveStore _store;

    public AzureBlobAppendTests()
    {
        var service = new Mock<BlobServiceClient>();
        service.Setup(s => s.GetBlobContainerClient(It.IsAny<string>())).Returns(_container.Object);
        _container.Protected()
            .Setup<AppendBlobClient>("GetAppendBlobClientCore", ItExpr.IsAny<string>())
            .Returns(_blob.Object);
        _container.Setup(c => c.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobContainerEncryptionScopeOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Response<BlobContainerInfo>)null!);
        _blob.Setup(b => b.CreateIfNotExistsAsync(It.IsAny<BlobHttpHeaders>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Response<BlobContentInfo>)null!);

        _store = new AzureBlobPayloadArchiveStore(service.Object, Options.Create(new PayloadCaptureOptions()));
    }

    [Fact]
    public async Task AppendLine_WhenBlobExists_ShouldAppendWithoutExistenceCalls()
    {
        SetupAppendOutcomes(null);

        await _store.AppendLineAsync("audit/2026-09-27/10/00/payload-audit.jsonl", "{}", CancellationToken.None);

        VerifyAppendCount(1);
        VerifyBlobCreated(Times.Never());
        VerifyContainerCreated(Times.Never());
    }

    [Fact]
    public async Task AppendLine_WhenBlobMissing_ShouldCreateBlobThenRetryAppend()
    {
        SetupAppendOutcomes("BlobNotFound");

        await _store.AppendLineAsync("audit/2026-09-27/10/00/payload-audit.jsonl", "{}", CancellationToken.None);

        VerifyAppendCount(2);
        VerifyBlobCreated(Times.Once());
        VerifyContainerCreated(Times.Never());
    }

    [Fact]
    public async Task AppendLine_WhenContainerMissing_ShouldCreateContainerAndBlobThenRetryAppend()
    {
        SetupAppendOutcomes("ContainerNotFound");

        await _store.AppendLineAsync("audit/2026-09-27/10/00/payload-audit.jsonl", "{}", CancellationToken.None);

        VerifyAppendCount(2);
        VerifyBlobCreated(Times.Once());
        VerifyContainerCreated(Times.Once());
    }

    [Fact]
    public async Task AppendLine_WhenAppendFailsForAnotherReason_ShouldPropagate()
    {
        SetupAppendOutcomes("AuthorizationPermissionMismatch");

        await Assert.ThrowsAsync<RequestFailedException>(() =>
            _store.AppendLineAsync("audit/2026-09-27/10/00/payload-audit.jsonl", "{}", CancellationToken.None));

        VerifyBlobCreated(Times.Never());
    }

    private void SetupAppendOutcomes(string? firstErrorCode)
    {
        var sequence = _blob.SetupSequence(b => b.AppendBlockAsync(It.IsAny<Stream>(), It.IsAny<AppendBlobAppendBlockOptions>(), It.IsAny<CancellationToken>()));
        if (firstErrorCode is not null)
            sequence = sequence.ThrowsAsync(new RequestFailedException(404, "missing", firstErrorCode, null));
        sequence.ReturnsAsync((Response<BlobAppendInfo>)null!);
    }

    private void VerifyAppendCount(int expected) =>
        _blob.Verify(b => b.AppendBlockAsync(It.IsAny<Stream>(), It.IsAny<AppendBlobAppendBlockOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(expected));

    private void VerifyBlobCreated(Times times) =>
        _blob.Verify(b => b.CreateIfNotExistsAsync(It.IsAny<BlobHttpHeaders>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()), times);

    private void VerifyContainerCreated(Times times) =>
        _container.Verify(c => c.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobContainerEncryptionScopeOptions>(), It.IsAny<CancellationToken>()), times);
}
