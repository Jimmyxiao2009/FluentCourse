using Microsoft.UI.Xaml.Media;

namespace FluentCourse.Runtime.Animations;

/// <summary>
/// 把元素上已有的 RenderTransform(Rotate/Scale/Translate/TransformGroup)的数值
/// 迁移进新建的 CompositeTransform,避免动画/转场粗暴替换变换时丢失页面 XAML
/// 中声明的旋转、缩放和平移。
/// </summary>
internal static class TransformMigration
{
    public static void Migrate(Transform? existing, CompositeTransform composite)
    {
        switch (existing)
        {
            case null:
            case CompositeTransform: // 调用方已保证不会传入,防御性跳过
                return;
            case RotateTransform r:
                composite.Rotation += r.Angle;
                composite.CenterX = r.CenterX;
                composite.CenterY = r.CenterY;
                return;
            case ScaleTransform s:
                composite.ScaleX *= s.ScaleX;
                composite.ScaleY *= s.ScaleY;
                composite.CenterX = s.CenterX;
                composite.CenterY = s.CenterY;
                return;
            case TranslateTransform t:
                composite.TranslateX += t.X;
                composite.TranslateY += t.Y;
                return;
            case TransformGroup g:
                // 尽力提取同类分量;复合矩阵语义无法完全等价,但远好于直接丢弃
                foreach (var child in g.Children)
                    Migrate(child, composite);
                return;
            default:
                System.Diagnostics.Debug.WriteLine(
                    $"[TransformMigration] 无法迁移变换类型 {existing.GetType().Name},其数值将丢失。");
                return;
        }
    }
}
