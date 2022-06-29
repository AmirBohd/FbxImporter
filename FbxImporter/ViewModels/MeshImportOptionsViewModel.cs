﻿using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SoulsAssetPipeline.FLVERImporting;

namespace FbxImporter.ViewModels;

public class MeshImportOptionsViewModel : ViewModelBase
{
    private readonly FLVER2MaterialInfoBank _materialInfoBank;

    public MeshImportOptionsViewModel(string meshName, FLVER2MaterialInfoBank materialInfoBank)
    {
        _materialInfoBank = materialInfoBank;
        Materials = new ObservableCollection<string>(materialInfoBank.MaterialDefs.Keys.Where(x => x != string.Empty).OrderBy(x => x));

        string[] meshNameParts = meshName.Split('|', StringSplitOptions.TrimEntries);
        SelectedMaterial = meshNameParts.Length > 1
            ? Materials.FirstOrDefault(x => string.Equals(x.Replace(".mtd", ""), meshNameParts[1].Replace(".mtd", ""), StringComparison.CurrentCultureIgnoreCase)) ?? Materials[0]
            : Materials[0];

        CancelCommand = ReactiveCommand.Create(Cancel);

        ConfirmCommand = ReactiveCommand.Create(Confirm);
    }
    
    [Reactive] public bool IsCloth { get; set; } = true;

    [Reactive] public bool CreateDefaultBone { get; set; } = true;

    [Reactive] public bool MirrorX { get; set; }  = true;

    public ObservableCollection<string> Materials { get; }

    [Reactive] public string SelectedMaterial { get; set; }

    public ReactiveCommand<Unit, MeshImportOptions> ConfirmCommand { get; }
    
    public ReactiveCommand<Unit, MeshImportOptions?> CancelCommand { get; }

    public MeshImportOptions? Cancel()
    {
        return null;
    }

    public MeshImportOptions Confirm()
    {
        return new MeshImportOptions
        {
            CreateDefaultBone = CreateDefaultBone,
            MirrorX = MirrorX,
            IsCloth = IsCloth,
            MTD = SelectedMaterial,
            MaterialInfoBank = _materialInfoBank
        };
    }
}