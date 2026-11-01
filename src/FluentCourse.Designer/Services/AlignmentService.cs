using System.Collections.Generic;
using System.Linq;
using FluentCourse.Designer.Models;

namespace FluentCourse.Designer.Services;

public sealed class AlignmentInfo
{
    public double TargetX { get; init; }
    public double TargetY { get; init; }
    public double LineX { get; init; } = double.NaN;
    public double LineY { get; init; } = double.NaN;
    public bool HasAlignment => !double.IsNaN(LineX) || !double.IsNaN(LineY);
}

public static class AlignmentService
{
    public const double SnapThreshold = 6.0;

    public static AlignmentInfo CalculateSnap(
        ElementModel moving,
        IEnumerable<ElementModel> others,
        double newX, double newY)
    {
        var movingRight = newX + moving.Width;
        var movingBottom = newY + moving.Height;
        var movingCenterX = newX + moving.Width / 2;
        var movingCenterY = newY + moving.Height / 2;

        var nearestEdgeDx = double.MaxValue;
        var nearestEdgeDy = double.MaxValue;
        double snapX = double.NaN;
        double snapY = double.NaN;
        double lineX = double.NaN;
        double lineY = double.NaN;

        foreach (var other in others)
        {
            if (ReferenceEquals(moving, other)) continue;
            if (other.Width <= 0 || other.Height <= 0) continue;

            var otherRight = other.X + other.Width;
            var otherBottom = other.Y + other.Height;
            var otherCenterX = other.X + other.Width / 2;
            var otherCenterY = other.Y + other.Height / 2;

            CheckSnap(movingRight, otherRight, ref snapX, ref lineX, ref nearestEdgeDx, otherRight);
            CheckSnap(newX, other.X, ref snapX, ref lineX, ref nearestEdgeDx, other.X);
            CheckSnap(newX, otherRight, ref snapX, ref lineX, ref nearestEdgeDx, otherRight);
            CheckSnap(movingRight, other.X, ref snapX, ref lineX, ref nearestEdgeDx, other.X);
            CheckSnap(movingCenterX, otherCenterX, ref snapX, ref lineX, ref nearestEdgeDx, otherCenterX);

            CheckSnap(movingBottom, otherBottom, ref snapY, ref lineY, ref nearestEdgeDy, otherBottom);
            CheckSnap(newY, other.Y, ref snapY, ref lineY, ref nearestEdgeDy, other.Y);
            CheckSnap(newY, otherBottom, ref snapY, ref lineY, ref nearestEdgeDy, otherBottom);
            CheckSnap(movingBottom, other.Y, ref snapY, ref lineY, ref nearestEdgeDy, other.Y);
            CheckSnap(movingCenterY, otherCenterY, ref snapY, ref lineY, ref nearestEdgeDy, otherCenterY);
        }

        return new AlignmentInfo
        {
            TargetX = double.IsNaN(snapX) ? newX : snapX,
            TargetY = double.IsNaN(snapY) ? newY : snapY,
            LineX = lineX,
            LineY = lineY
        };
    }

    private static void CheckSnap(
        double movingEdge, double otherEdge,
        ref double snap, ref double line,
        ref double nearestDist, double linePos)
    {
        var dist = System.Math.Abs(movingEdge - otherEdge);
        if (dist <= SnapThreshold && dist < nearestDist)
        {
            snap = otherEdge;
            line = linePos;
            nearestDist = dist;
        }
    }
}
