using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace FastFluentFilesFolders.ViewModels
{
    public partial class TagViewModel : ObservableObject
    {
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _fontIconGlyph = string.Empty;
        [ObservableProperty] private string _imageIconPath = string.Empty;
	}
}
