using Xunit;

namespace NIFSharp.Tests;

/// <summary>
/// Reading a NIF and writing it back.
/// </summary>
/// <remarks>
/// This is the only check that means much for a file format reader: not that the
/// library agrees with itself about what a block holds, but that a file it has
/// read and written is the file it was given, byte for byte. Anything the schema
/// describes badly, any field skipped, any padding invented, shows up here and
/// nowhere else.
///
/// The fixtures are nifly's, committed alongside, so this runs anywhere.
/// </remarks>
public class RoundTripTests
{
    private static readonly NifXmlDatabase Db = NifXmlDatabase.LoadEmbedded();

    private static string ResourceRoot => Path.Combine(AppContext.BaseDirectory, "Resources");

    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();

        foreach (string path in Directory
            .GetFiles(ResourceRoot, "*.nif", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => Path.GetRelativePath(ResourceRoot, f))
            .Where(FixtureFiles.IsFixture))
        {
            data.Add(path);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void LoadsWithoutWarnings(string relative)
    {
        NifModel model = NifModel.Load(Path.Combine(ResourceRoot, relative), Db);

        Assert.Empty(model.Warnings);
        Assert.NotEmpty(model.Blocks);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void SavingReproducesTheFileByteForByte(string relative)
    {
        string path = Path.Combine(ResourceRoot, relative);

        NifModel model = NifModel.Load(path, Db);

        using var written = new MemoryStream();
        model.Save(written);

        byte[] original = File.ReadAllBytes(path);
        byte[] again = written.ToArray();

        Assert.Equal(original.Length, again.Length);

        // Report where, rather than dumping a megabyte of hex at whoever broke it.
        for (int i = 0; i < original.Length; i++)
        {
            if (original[i] != again[i])
                Assert.Fail($"{relative}: byte {i} was 0x{original[i]:X2}, wrote 0x{again[i]:X2}");
        }
    }

    [Fact]
    public void ACorruptFileFailsToLoadRatherThanReadingRubbish()
    {
        string path = Path.Combine(ResourceRoot, "nifly", "TestNifFile_Corrupted.nif");

        if (!File.Exists(path)) return;

        Assert.ThrowsAny<Exception>(() => NifModel.Load(path, Db));
    }
}
