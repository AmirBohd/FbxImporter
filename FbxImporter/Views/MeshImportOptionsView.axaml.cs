﻿using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using FbxImporter.ViewModels;
using ReactiveUI;
using System;

namespace FbxImporter.Views;

public partial class MeshImportOptionsView : ReactiveWindow<MeshImportOptionsViewModel>
{
    public MeshImportOptionsView()
    {
        InitializeComponent();
        this.WhenActivated(d =>
        {
            d(ViewModel!.ConfirmCommand.Subscribe(Close));
            d(ViewModel!.CancelCommand.Subscribe(Close));
        });
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}