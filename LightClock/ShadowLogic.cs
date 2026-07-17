namespace LightClock;

internal static class ShadowLogic
{
    internal const int DefaultShadowOffsetPx = 3;

    internal static int GetScaledShadowOffset(int shadowOpacity, int baseOffsetPx, int dpi)
    {
        if (shadowOpacity <= 0 || baseOffsetPx <= 0)
        {
            return 0;
        }

        int clampedDpi = dpi > 0 ? dpi : 96;
        return Math.Max(1, (baseOffsetPx * clampedDpi + 48) / 96);
    }

    internal static (int Opacity, int BaseOffsetPx) ResolveFromSettings(int? shadowOpacity, int? shadowOffsetPx, int? shadowLevel)
    {
        // New format has highest priority and is unambiguous:
        // shadowOpacity is always interpreted as 0-100 (%), never as legacy level.
        if (shadowOpacity.HasValue && shadowOpacity.Value >= 0 && shadowOpacity.Value <= 100)
        {
            int resolvedOffset = (shadowOffsetPx.HasValue && shadowOffsetPx.Value > 0)
                ? shadowOffsetPx.Value
                : DefaultShadowOffsetPx;
            return (shadowOpacity.Value, resolvedOffset);
        }

        // Legacy format: shadowLevel 0-3 (offset-based).
        if (shadowLevel.HasValue && shadowLevel.Value >= 0 && shadowLevel.Value <= 3)
        {
            int level = shadowLevel.Value;
            return (MapLegacyShadowLevelToOpacity(level), MapLegacyShadowLevelToOffsetPx(level));
        }

        return (0, DefaultShadowOffsetPx);
    }

    internal static int MapLegacyShadowLevelToOpacity(int level) => level switch
    {
        0 => 0,
        1 => 25,
        2 => 50,
        3 => 100,
        _ => 0
    };

    internal static int MapLegacyShadowLevelToOffsetPx(int level) => level switch
    {
        0 => 0,
        1 => 2,
        2 => 4,
        3 => 6,
        _ => 0
    };

    internal static int ComputeAlphaFromBlend(
        byte pixelR,
        byte pixelG,
        byte pixelB,
        byte fgR,
        byte fgG,
        byte fgB,
        byte bgR,
        byte bgG,
        byte bgB,
        int maxAlpha)
    {
        if (maxAlpha <= 0)
        {
            return 0;
        }

        int diffR = Math.Abs(fgR - bgR);
        int diffG = Math.Abs(fgG - bgG);
        int diffB = Math.Abs(fgB - bgB);
        int alphaChannel = diffG >= diffR && diffG >= diffB ? 1 : (diffR >= diffB ? 0 : 2);

        int fg = alphaChannel == 0 ? fgR : (alphaChannel == 1 ? fgG : fgB);
        int bg = alphaChannel == 0 ? bgR : (alphaChannel == 1 ? bgG : bgB);
        int px = alphaChannel == 0 ? pixelR : (alphaChannel == 1 ? pixelG : pixelB);

        int denom = fg - bg;
        if (denom == 0)
        {
            return 0;
        }

        int alpha = ((px - bg) * maxAlpha) / denom;
        if (alpha < 0) return 0;
        if (alpha > maxAlpha) return maxAlpha;
        return alpha;
    }
}
