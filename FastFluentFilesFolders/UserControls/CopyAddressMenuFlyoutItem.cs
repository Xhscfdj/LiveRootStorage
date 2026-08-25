using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FastFluentFilesFolders.UserControls
{
    /// <summary>
    /// 支持自定义内容的菜单项：Content 可承载任意 UIElement（如 ThemedIcon + 文本），
    /// 模板在右侧以灰色小字显示 KeyboardAcceleratorTextOverride 快捷键提示。
    /// </summary>
    public sealed class CopyAddressMenuFlyoutItem : MenuFlyoutItem
    {
        public CopyAddressMenuFlyoutItem()
        {
            Style = (Style)Application.Current.Resources["CopyAddressMenuFlyoutItemStyle"];
        }

        public static readonly DependencyProperty ContentProperty =
            DependencyProperty.Register(nameof(Content), typeof(object), typeof(CopyAddressMenuFlyoutItem),
                new PropertyMetadata(null));

        public object Content
        {
            get => (object)GetValue(ContentProperty);
            set => SetValue(ContentProperty, value);
        }
    }
}
