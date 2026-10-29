using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

/// <summary>Port of clsDX8Engine.ShowNextFrame (GRH frame advance by speed).</summary>
internal static class GraphicAnimationService
{
    public static bool ShowNextFrame(GraphicData graphic, ref GraphicAnimation animation, float deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(graphic);
        if (graphic.Frames.Length <= 1 || graphic.Speed <= 0) return false;
        animation.FrameCounter += deltaSeconds;
        var frameDuration = 1f / graphic.Speed;
        if (animation.FrameCounter < frameDuration) return false;
        animation.FrameCounter -= frameDuration;
        return true;
    }
}
