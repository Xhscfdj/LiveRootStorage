using System;
using FastFluentFilesFolders.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace FastFluentFilesFolders.UserControls
{
    /// <summary>
    /// Maps a <see cref="FileOperationState"/> to a theme brush.
    /// Brushes are injected via XAML <c>{ThemeResource ...}</c> bindings on the 8
    /// DependencyProperties; no runtime resource lookup is performed.
    /// Pass the string parameter "Background" to select the background brush variant.
    /// </summary>
    public sealed partial class FileOperationStateToBrushConverter : DependencyObject, IValueConverter
    {
        public static readonly DependencyProperty InProgressBrushProperty =
            DependencyProperty.Register(nameof(InProgressBrush), typeof(SolidColorBrush), typeof(FileOperationStateToBrushConverter), new PropertyMetadata(null));
        public static readonly DependencyProperty InProgressBackgroundBrushProperty =
            DependencyProperty.Register(nameof(InProgressBackgroundBrush), typeof(SolidColorBrush), typeof(FileOperationStateToBrushConverter), new PropertyMetadata(null));
        public static readonly DependencyProperty SuccessfulBrushProperty =
            DependencyProperty.Register(nameof(SuccessfulBrush), typeof(SolidColorBrush), typeof(FileOperationStateToBrushConverter), new PropertyMetadata(null));
        public static readonly DependencyProperty SuccessfulBackgroundBrushProperty =
            DependencyProperty.Register(nameof(SuccessfulBackgroundBrush), typeof(SolidColorBrush), typeof(FileOperationStateToBrushConverter), new PropertyMetadata(null));
        public static readonly DependencyProperty ErrorBrushProperty =
            DependencyProperty.Register(nameof(ErrorBrush), typeof(SolidColorBrush), typeof(FileOperationStateToBrushConverter), new PropertyMetadata(null));
        public static readonly DependencyProperty ErrorBackgroundBrushProperty =
            DependencyProperty.Register(nameof(ErrorBackgroundBrush), typeof(SolidColorBrush), typeof(FileOperationStateToBrushConverter), new PropertyMetadata(null));
        public static readonly DependencyProperty CanceledBrushProperty =
            DependencyProperty.Register(nameof(CanceledBrush), typeof(SolidColorBrush), typeof(FileOperationStateToBrushConverter), new PropertyMetadata(null));
        public static readonly DependencyProperty CanceledBackgroundBrushProperty =
            DependencyProperty.Register(nameof(CanceledBackgroundBrush), typeof(SolidColorBrush), typeof(FileOperationStateToBrushConverter), new PropertyMetadata(null));

        public SolidColorBrush InProgressBrush
        {
            get => (SolidColorBrush)GetValue(InProgressBrushProperty);
            set => SetValue(InProgressBrushProperty, value);
        }
        public SolidColorBrush InProgressBackgroundBrush
        {
            get => (SolidColorBrush)GetValue(InProgressBackgroundBrushProperty);
            set => SetValue(InProgressBackgroundBrushProperty, value);
        }
        public SolidColorBrush SuccessfulBrush
        {
            get => (SolidColorBrush)GetValue(SuccessfulBrushProperty);
            set => SetValue(SuccessfulBrushProperty, value);
        }
        public SolidColorBrush SuccessfulBackgroundBrush
        {
            get => (SolidColorBrush)GetValue(SuccessfulBackgroundBrushProperty);
            set => SetValue(SuccessfulBackgroundBrushProperty, value);
        }
        public SolidColorBrush ErrorBrush
        {
            get => (SolidColorBrush)GetValue(ErrorBrushProperty);
            set => SetValue(ErrorBrushProperty, value);
        }
        public SolidColorBrush ErrorBackgroundBrush
        {
            get => (SolidColorBrush)GetValue(ErrorBackgroundBrushProperty);
            set => SetValue(ErrorBackgroundBrushProperty, value);
        }
        public SolidColorBrush CanceledBrush
        {
            get => (SolidColorBrush)GetValue(CanceledBrushProperty);
            set => SetValue(CanceledBrushProperty, value);
        }
        public SolidColorBrush CanceledBackgroundBrush
        {
            get => (SolidColorBrush)GetValue(CanceledBackgroundBrushProperty);
            set => SetValue(CanceledBackgroundBrushProperty, value);
        }

        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not FileOperationState state)
            {
                return null;
            }

            bool isBackground = parameter is string s && s == "Background";

            return (state, isBackground) switch
            {
                (FileOperationState.InProgress, false) => InProgressBrush,
                (FileOperationState.InProgress, true) => InProgressBackgroundBrush,
                (FileOperationState.Successful, false) => SuccessfulBrush,
                (FileOperationState.Successful, true) => SuccessfulBackgroundBrush,
                (FileOperationState.Error, false) => ErrorBrush,
                (FileOperationState.Error, true) => ErrorBackgroundBrush,
                (_, false) => CanceledBrush,
                (_, true) => CanceledBackgroundBrush,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}