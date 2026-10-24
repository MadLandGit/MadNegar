namespace MadNegar;

internal static class ModelCatalog
{
    public static readonly List<ModelInfo> Models =
    [
        new("tiny", "75 MiB"),
        new("tiny.en", "75 MiB", true),
        new("tiny-q5_1", "31 MiB"),
        new("tiny.en-q5_1", "31 MiB", true),
        new("tiny-q8_0", "42 MiB"),
        new("tiny.en-q8_0", "42 MiB", true),

        new("base", "142 MiB"),
        new("base.en", "142 MiB", true),
        new("base-q5_1", "57 MiB"),
        new("base.en-q5_1", "57 MiB", true),
        new("base-q8_0", "78 MiB"),
        new("base.en-q8_0", "78 MiB", true),

        new("small", "466 MiB"),
        new("small.en", "466 MiB", true),
        new("small-q5_1", "181 MiB"),
        new("small.en-q5_1", "181 MiB", true),
        new("small-q8_0", "252 MiB"),
        new("small.en-q8_0", "252 MiB", true),

        new("medium", "1.5 GiB"),
        new("medium.en", "1.5 GiB", true),
        new("medium-q5_0", "514 MiB"),
        new("medium.en-q5_0", "514 MiB", true),
        new("medium-q8_0", "785 MiB"),
        new("medium.en-q8_0", "785 MiB", true),

        new("large-v1", "2.9 GiB"),
        new("large-v2", "2.9 GiB"),
        new("large-v2-q5_0", "1.1 GiB"),
        new("large-v2-q8_0", "1.5 GiB"),
        new("large-v3", "2.9 GiB"),
        new("large-v3-q5_0", "1.1 GiB"),
        new("large-v3-q8_0", "1.5 GiB"),
        new("large-v3-turbo", "1.5 GiB"),
        new("large-v3-turbo-q5_0", "547 MiB"),
        new("large-v3-turbo-q8_0", "834 MiB"),
    ];
}
