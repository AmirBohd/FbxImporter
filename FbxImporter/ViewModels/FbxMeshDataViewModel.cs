﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json;
using SoulsFormats;

namespace FbxImporter.ViewModels;

public class FbxMeshDataViewModel
{
    public FbxMeshDataViewModel(string name)
    {
        Name = name;
    }

    public string Name { get; set; }

    // ReSharper disable once CollectionNeverUpdated.Local
    [JsonProperty]
    private List<FbxVertexData> VertexData { get; set; } = new();

    [JsonProperty]
    private List<int> VertexIndices { get; set; } = new();

    public FlverMeshViewModel ToFlverMesh(FLVER2 flver, MeshImportOptions options)
    {
        string[] nameParts = Name.Split('|', StringSplitOptions.TrimEntries);
        string name = nameParts.Length > 1 ? nameParts[0] : Name;
        FLVER2.Material newMaterial = new()
        {
            Name = name,
            MTD = options.MTD,
            Textures = new List<FLVER2.Texture>(options.MaterialInfoBank.MaterialDefs[options.MTD].TextureChannels.Values.Select(x => new FLVER2.Texture { Type = x }))
        };
        
        FLVER2.GXList gxList = new();
        gxList.AddRange(options.MaterialInfoBank.GetDefaultGXItemsForMTD(options.MTD));

        List<FLVER2.BufferLayout> bufferLayouts =
            options.MaterialInfoBank.MaterialDefs[options.MTD].AcceptableVertexBufferDeclarations.FirstOrDefault(x =>
                x.Buffers.SelectMany(y => y).Count(y => y.Semantic == FLVER.LayoutSemantic.Tangent) >=
                VertexData[0].Tangents.Count)?.Buffers ?? options.MaterialInfoBank.MaterialDefs[options.MTD]
                .AcceptableVertexBufferDeclarations[0].Buffers;

        List<int> layoutIndices = GetLayoutIndices(flver, bufferLayouts);

        FLVER2.Mesh newMesh = new()
        {
            VertexBuffers = layoutIndices.Select(x => new FLVER2.VertexBuffer(x)).ToList(),
            MaterialIndex = flver.Materials.Count,
            Dynamic = 1
        };

        int defaultBoneIndex = flver.Bones.IndexOf(flver.Bones.FirstOrDefault(x => x.Name == this.Name));
        if (defaultBoneIndex == -1)
        {
            if (options.CreateDefaultBone)
            {
                flver.Bones.Add(new FLVER.Bone {Name = this.Name});
                defaultBoneIndex = flver.Bones.Count - 1;
            }
            else
            {
                defaultBoneIndex = 0;
            }
        }

        newMesh.DefaultBoneIndex = defaultBoneIndex;

        foreach (FbxVertexData vertexData in VertexData)
        {
            FLVER.VertexBoneIndices boneIndices = new();
            FLVER.VertexBoneWeights boneWeights = new();
            for (int j = 0; j < 4; j++)
            {
                int boneIndex = GetBoneIndexFromName(flver, vertexData.BoneNames[j]);
                boneIndices[j] = boneIndex;
                boneWeights[j] = vertexData.BoneWeights[j];
            }

            int xSign = options.MirrorX ? -1 : 1;

            FLVER.Vertex newVertex = new()
            {
                Position = new Vector3(xSign * vertexData.Position[0], vertexData.Position[1], vertexData.Position[2]),
                Normal = new Vector3(xSign * vertexData.Normal[0], vertexData.Normal[1], vertexData.Normal[2]),
                Bitangent = new Vector4(-1, -1, -1, -1),
                Tangents = new List<Vector4>(vertexData.Tangents.Select(x => new Vector4(xSign * x[0], x[1], x[2], x[3]))),
                UVs = new List<Vector3>(vertexData.UVs.Select(x => new Vector3(x[0], 1-x[1], x[2]))),
                BoneIndices = boneIndices,
                BoneWeights = boneWeights
            };

            PadVertex(newVertex, bufferLayouts);

            newMesh.Vertices.Add(newVertex);
        }

        if (!options.MirrorX)
        {
            FlipFaceSet();
        }

        FLVER2.FaceSet.FSFlags[] faceSetFlags =
        {
            FLVER2.FaceSet.FSFlags.None,
            FLVER2.FaceSet.FSFlags.LodLevel1,
            FLVER2.FaceSet.FSFlags.LodLevel2,
            FLVER2.FaceSet.FSFlags.MotionBlur,
            FLVER2.FaceSet.FSFlags.MotionBlur | FLVER2.FaceSet.FSFlags.LodLevel1,
            FLVER2.FaceSet.FSFlags.MotionBlur | FLVER2.FaceSet.FSFlags.LodLevel2
        };

        List<FLVER2.FaceSet> faceSets = new();

        if (VertexIndices.Contains(-1))
        {
            throw new InvalidDataException($"Negative vertex index found in fbx mesh {Name}");
        }

        foreach (FLVER2.FaceSet.FSFlags faceSetFlag in faceSetFlags)
        {
            faceSets.Add(new FLVER2.FaceSet
            {
                Indices = VertexIndices,
                CullBackfaces = false,
                Flags = faceSetFlag,
                TriangleStrip = false
            });
        }

        newMesh.FaceSets.AddRange(faceSets);
            
        FlverMeshViewModel output = new(newMesh, newMaterial, gxList);
        return output;
    }

    private static void PadVertex(FLVER.Vertex vertex, IEnumerable<FLVER2.BufferLayout> bufferLayouts)
    {
        Dictionary<FLVER.LayoutSemantic, int> usageCounts = new();
        FLVER.LayoutSemantic[] paddedProperties =
            {FLVER.LayoutSemantic.Tangent, FLVER.LayoutSemantic.UV, FLVER.LayoutSemantic.VertexColor};

        IEnumerable<FLVER.LayoutMember> layoutMembers = bufferLayouts.SelectMany(bufferLayout => bufferLayout)
            .Where(x => paddedProperties.Contains(x.Semantic));
        foreach (FLVER.LayoutMember layoutMember in layoutMembers)
        {
            bool isDouble = layoutMember.Semantic == FLVER.LayoutSemantic.UV &&
                            layoutMember.Type is FLVER.LayoutType.Float4 or FLVER.LayoutType.UVPair;
            int count = isDouble ? 2 : 1;
                
            if (usageCounts.ContainsKey(layoutMember.Semantic))
            {
                usageCounts[layoutMember.Semantic] += count;
            }
            else
            {
                usageCounts.Add(layoutMember.Semantic, count);
            }
        }

        if (usageCounts.ContainsKey(FLVER.LayoutSemantic.Tangent))
        {
            int missingTangentCount = usageCounts[FLVER.LayoutSemantic.Tangent] - vertex.Tangents.Count;
            for (int i = 0; i < missingTangentCount; i++)
            {
                vertex.Tangents.Add(Vector4.Zero);
            }
        }
        
        if (usageCounts.ContainsKey(FLVER.LayoutSemantic.UV))
        {
            int missingUvCount = usageCounts[FLVER.LayoutSemantic.UV] - vertex.UVs.Count;
            for (int i = 0; i < missingUvCount; i++)
            {
                vertex.UVs.Add(Vector3.Zero);
            }
        }
        
        if (usageCounts.ContainsKey(FLVER.LayoutSemantic.VertexColor))
        {
            int missingColorCount = usageCounts[FLVER.LayoutSemantic.VertexColor] - vertex.Colors.Count;
            for (int i = 0; i < missingColorCount; i++)
            {
                vertex.Colors.Add(new FLVER.VertexColor(255, 255, 0, 255));
            }
        }
    }

    private static List<int> GetLayoutIndices(FLVER2 flver, List<FLVER2.BufferLayout> bufferLayouts)
    {
        List<int> indices = new();

        foreach (FLVER2.BufferLayout referenceBufferLayout in bufferLayouts)
        {
            for (int i = 0; i < flver.BufferLayouts.Count; i++)
            {
                FLVER2.BufferLayout bufferLayout = flver.BufferLayouts[i];
                if (bufferLayout.Select(x => (x.Type, x.Semantic)).SequenceEqual(referenceBufferLayout.Select(x => (x.Type, x.Semantic))))
                {
                    indices.Add(i);
                    break;
                }

                if (i == flver.BufferLayouts.Count - 1)
                {
                    indices.Add(i + 1);
                    flver.BufferLayouts.Add(referenceBufferLayout);
                    break;
                }
            }
        }

        return indices;
    }

    private void FlipFaceSet()
    {
        for (int i = 0; i < VertexIndices.Count; i += 3)
        {
            (VertexIndices[i + 1], VertexIndices[i + 2]) = (VertexIndices[i + 2], VertexIndices[i + 1]);
        }
    }

    private static int GetBoneIndexFromName(FLVER2 flver, string boneName)
    {
        if (boneName == "0")
        {
            return 0;
        }

        int boneIndex = flver.Bones.IndexOf(flver.Bones.FirstOrDefault(x => x.Name == boneName));
        if (boneIndex != -1)
        {
            return boneIndex;
        }
            
        Logger.Log($"No Bone with name {boneName} found, bone index set to 0");
            
        return 0;

    }
}