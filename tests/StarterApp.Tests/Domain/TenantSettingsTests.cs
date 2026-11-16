namespace StarterApp.Tests.Domain;

public class TenantSettingsTests
{
    [Theory]
    [InlineData("sky", true)]
    [InlineData("high-contrast-2", true)]
    [InlineData("", false)]
    [InlineData("Sky", false)]
    [InlineData("2sky", false)]
    [InlineData("sky blue", false)]
    [InlineData("sky\n", false)]
    [InlineData(null, false)]
    public void APaletteIsALowerCaseName(string? palette, bool expected)
    {
        Assert.Equal(expected, TenantSettings.IsPaletteName(palette));
    }

    [Fact]
    public void ChoosingAPalette_RecordsWhoChoseIt()
    {
        var settings = new TenantSettings("tenant-1", "sky", "first-admin");

        settings.ChoosePalette("coral", "second-admin");

        Assert.Equal("coral", settings.Palette);
        Assert.Equal("second-admin", settings.UpdatedBy);
        Assert.Equal("tenant-1", settings.TenantId);
    }

    [Fact]
    public void ANameThatIsNotAPalette_IsRefused()
    {
        var settings = new TenantSettings("tenant-1", "sky", "admin");

        Assert.Throws<ArgumentException>(() => settings.ChoosePalette("Sky Blue", "admin"));
        Assert.Equal("sky", settings.Palette);
    }
}
