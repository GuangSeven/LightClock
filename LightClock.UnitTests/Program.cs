using LightClock;

var tests = new (string Name, Action Run)[]
{
    ("Legacy level 3 maps to full opacity + 6px", () =>
    {
        var resolved = ShadowLogic.ResolveFromSettings(null, null, 3);
        AssertEqual(100, resolved.Opacity, "opacity");
        AssertEqual(6, resolved.BaseOffsetPx, "offset");
    }),
    ("shadowOpacity keeps literal 3% (no legacy reinterpretation)", () =>
    {
        var resolved = ShadowLogic.ResolveFromSettings(3, null, null);
        AssertEqual(3, resolved.Opacity, "opacity");
        AssertEqual(ShadowLogic.DefaultShadowOffsetPx, resolved.BaseOffsetPx, "offset");
    }),
    ("New keys override legacy key", () =>
    {
        var resolved = ShadowLogic.ResolveFromSettings(50, 7, 3);
        AssertEqual(50, resolved.Opacity, "opacity");
        AssertEqual(7, resolved.BaseOffsetPx, "offset");
    }),
    ("Red text blend keeps visible alpha", () =>
    {
        int alpha = ShadowLogic.ComputeAlphaFromBlend(
            pixelR: 255, pixelG: 0, pixelB: 128,
            fgR: 255, fgG: 0, fgB: 0,
            bgR: 255, bgG: 0, bgB: 255,
            maxAlpha: 255);
        AssertTrue(alpha > 0, "alpha should be > 0");
    }),
    ("Blue text blend keeps visible alpha", () =>
    {
        int alpha = ShadowLogic.ComputeAlphaFromBlend(
            pixelR: 128, pixelG: 0, pixelB: 255,
            fgR: 0, fgG: 0, fgB: 255,
            bgR: 255, bgG: 0, bgB: 255,
            maxAlpha: 255);
        AssertTrue(alpha > 0, "alpha should be > 0");
    }),
    ("Offset scales from base px", () =>
    {
        AssertEqual(3, ShadowLogic.GetScaledShadowOffset(100, 3, 96), "96 DPI");
        AssertEqual(6, ShadowLogic.GetScaledShadowOffset(100, 6, 96), "legacy 6px at 96 DPI");
        AssertEqual(0, ShadowLogic.GetScaledShadowOffset(0, 3, 96), "shadow disabled");
    })
};

int failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS: {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL: {test.Name} => {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static void AssertEqual<T>(T expected, T actual, string field)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{field}: expected {expected}, got {actual}");
    }
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
