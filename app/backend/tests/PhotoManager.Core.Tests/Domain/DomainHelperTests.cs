using PhotoManager.Core.Crypto;
using PhotoManager.Core.Domain;

namespace PhotoManager.Core.Tests.Domain;

public sealed class DomainHelperTests
{
    [Theory]
    [InlineData("2024-05-01/IMG_1234.jpg.encrypted", true, EncryptionFormat.LegacyV1)]
    [InlineData("2024-05-01/woman_and_boy_smiling_on_beach.jpeg.encrypted", true, EncryptionFormat.LegacyV1)]
    [InlineData("v2/2026/11/0f8fad5bd9cb469fa165708167e1fc33.pmv2", true, EncryptionFormat.V2)]
    [InlineData("v2/tmp/test.pmv2", false, EncryptionFormat.V2)]
    [InlineData("2024-05-01/", false, default(EncryptionFormat))]
    [InlineData("notes.txt", false, default(EncryptionFormat))]
    public void Classifies_bucket_keys(string key, bool expected, EncryptionFormat expectedFormat)
    {
        var result = ArchiveObjectKeys.TryClassify(key, out var format);

        Assert.Equal(expected, result);
        if (expected)
        {
            Assert.Equal(expectedFormat, format);
        }
    }

    [Fact]
    public void V2_keys_round_trip_the_photo_id_and_contain_no_filename()
    {
        var id = Guid.NewGuid();
        var key = ArchiveObjectKeys.ForV2(id, new DateTimeOffset(2026, 11, 19, 23, 0, 0, TimeSpan.FromHours(-6)));

        Assert.Equal($"v2/2026/11/{id:N}.pmv2", key); // 23:00 -06:00 is still November in UTC
        Assert.True(ArchiveObjectKeys.TryParseV2PhotoId(key, out var parsed));
        Assert.Equal(id, parsed);
    }

    [Fact]
    public void Parses_legacy_upload_dates_and_names()
    {
        const string key = "2023-07-04/fireworks_over_lake.jpg.encrypted";

        Assert.Equal(new DateTimeOffset(2023, 7, 4, 0, 0, 0, TimeSpan.Zero), ArchiveObjectKeys.LegacyUploadDate(key));
        Assert.Equal("fireworks_over_lake.jpg", ArchiveObjectKeys.LegacyFileName(key));
        Assert.Equal("image/jpeg", ArchiveObjectKeys.GuessContentType("fireworks_over_lake.jpg"));
        Assert.Null(ArchiveObjectKeys.LegacyUploadDate("photo.jpg.encrypted"));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 }, "image/png")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0, 0, 0, 0, 0, 0 }, "image/gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0, 3, 0, 0, 0, 4, 0, 0, 0 }, null)] // Windows executable
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67, 0x20, 0, 0, 0, 0, 0, 0, 0 }, null)] // "<svg " (script-capable)
    public void Detects_images_from_content_not_names(byte[] prefix, string? expected)
    {
        Assert.Equal(expected, ImageSignatures.Detect(prefix)?.ContentType);
    }

    [Theory]
    [InlineData(@"C:\Users\me\Pictures\IMG 1.jpg", "IMG 1.jpg")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("photo\u202Egpj.exe", "photogpj.exe")]
    [InlineData("  bad<>:\"|?*name.png  ", "badname.png")]
    [InlineData("line\r\nbreak.jpg", "linebreak.jpg")]
    [InlineData("...", "photo")]
    [InlineData(".. ..", "photo")]
    [InlineData("photo. .", "photo")]
    [InlineData(". .jpg", "jpg")]
    [InlineData(null, "photo")]
    public void Sanitizes_untrusted_file_names(string? input, string expected)
    {
        Assert.Equal(expected, FileNames.Sanitize(input));
    }

    [Fact]
    public void Limits_file_name_length()
    {
        Assert.Equal(FileNames.MaxLength, FileNames.Sanitize(new string('a', 1000) + ".jpg").Length);

        var surrogateEdge = FileNames.Sanitize(new string('a', FileNames.MaxLength - 1) + "\U0001F4F7");
        Assert.Equal(FileNames.MaxLength - 1, surrogateEdge.Length);
        Assert.False(char.IsHighSurrogate(surrogateEdge[^1]));

        var withWebp = FileNames.WithExtension(FileNames.Sanitize(new string('a', 250)), ".webp");
        Assert.Equal(FileNames.MaxLength, withWebp.Length);
        Assert.EndsWith(".webp", withWebp, StringComparison.Ordinal);

        var surrogateWithExt = FileNames.WithExtension(new string('a', FileNames.MaxLength - 6) + "\U0001F4F7.png", ".webp");
        Assert.Equal(FileNames.MaxLength - 1, surrogateWithExt.Length);
        Assert.EndsWith(".webp", surrogateWithExt, StringComparison.Ordinal);
        Assert.False(char.IsHighSurrogate(surrogateWithExt[FileNames.MaxLength - 7]));

        var trailingSpaceTruncated = FileNames.WithExtension(new string('a', FileNames.MaxLength - 6) + " .png", ".webp");
        Assert.Equal(new string('a', FileNames.MaxLength - 6) + ".webp", trailingSpaceTruncated);

        var longJpeg = FileNames.WithExtension(new string('a', 250) + ".jpeg", ".jpg");
        Assert.Equal(FileNames.MaxLength, longJpeg.Length);
        Assert.EndsWith(".jpeg", longJpeg, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("woman and boy smiling on the beach", "IMG_1234.JPG", "woman_and_boy_smiling_on_the_beach.jpg")]
    [InlineData("Sunset Over The LAKE", "DSC_0042.JPEG", "sunset_over_the_lake.jpeg")]
    [InlineData("3 kids on 2 bikes", "a.webp", "3_kids_on_2_bikes.webp")]
    [InlineData("children's birthday party", "x.jpg", "childrens_birthday_party.jpg")]
    [InlineData("grandma\u2019s garden", "x.jpg", "grandmas_garden.jpg")]
    [InlineData("café crème on a table", "x.jpg", "café_crème_on_a_table.jpg")]
    [InlineData("cafe\u0301 au lait", "x.jpg", "cafe\u0301_au_lait.jpg")]
    [InlineData("dog looking up at", "x.jpg", "dog_looking_up_at.jpg")] // only a shortened name loses trailing filler words
    // Shortened at a word to 60 characters at most, without the "in" it would have ended on.
    [InlineData("grandparents and grandchildren having a picnic together in the park on a sunny afternoon", "x.png", "grandparents_and_grandchildren_having_a_picnic_together.png")]
    // Untrusted model output: separators, dots, quotes and bidi overrides cannot survive into the name.
    [InlineData("mother/daughter: ../../etc \"hike\"?", "trip.jpeg", "mother_daughter_etc_hike.jpeg")]
    [InlineData("photo\u202Egpj.exe", "x.jpg", "photo_gpj_exe.jpg")]
    [InlineData("con", "x.jpg", "con_photo.jpg")]
    [InlineData("LPT1", "x.jpg", "lpt1_photo.jpg")]
    [InlineData("com", "x.jpg", "com.jpg")]
    // Only a plain extension is carried over.
    [InlineData("sunset", "0f8fad5bd9cb469fa165708167e1fc33", "sunset")]
    [InlineData("sunset", "holiday.final version", "sunset")]
    [InlineData("sunset", null, "sunset")]
    // Nothing to name the photo after.
    [InlineData("🌅 🌊 !!!", "x.jpg", null)]
    [InlineData("   ", "x.jpg", null)]
    [InlineData(null, "x.jpg", null)]
    public void Names_photos_after_their_description(string? description, string? currentFileName, string? expected)
    {
        var name = FileNames.FromDescription(description, currentFileName);

        Assert.Equal(expected, name);
        if (name is not null)
        {
            Assert.Equal(name, FileNames.Sanitize(name));
            Assert.True(Path.GetFileNameWithoutExtension(name).Length <= FileNames.MaxDescriptiveStemLength);
        }
    }

    [Fact]
    public void A_description_that_is_one_overlong_word_is_cut()
    {
        Assert.Equal(new string('a', FileNames.MaxDescriptiveStemLength) + ".jpg", FileNames.FromDescription(new string('a', 70), "x.jpg"));
    }

    [Fact]
    public void Archive_keys_only_belong_to_the_photo_they_were_written_for()
    {
        var photo = Guid.NewGuid();
        var other = Guid.NewGuid();
        var uploadedAt = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

        Assert.True(ArchiveObjectKeys.BelongsTo(ArchiveObjectKeys.ForV2(photo, uploadedAt), photo, EncryptionFormat.V2));
        Assert.True(ArchiveObjectKeys.BelongsTo("2019-07-04/IMG_0001.jpg.encrypted", photo, EncryptionFormat.LegacyV1));

        // Another photo's v2 file, a format that does not match the record, and objects that are not archive files.
        Assert.False(ArchiveObjectKeys.BelongsTo(ArchiveObjectKeys.ForV2(other, uploadedAt), photo, EncryptionFormat.V2));
        Assert.False(ArchiveObjectKeys.BelongsTo(ArchiveObjectKeys.ForV2(photo, uploadedAt), photo, EncryptionFormat.LegacyV1));
        Assert.False(ArchiveObjectKeys.BelongsTo("2019-07-04/IMG_0001.jpg.encrypted", photo, EncryptionFormat.V2));
        Assert.False(ArchiveObjectKeys.BelongsTo("backups/database.dump", photo, EncryptionFormat.LegacyV1));
        Assert.False(ArchiveObjectKeys.BelongsTo($"v2/2026/11/{photo:N}.txt", photo, EncryptionFormat.V2));
    }
}
