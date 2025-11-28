using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Assets.Export;
using AssetRipper.Export.UnityProjects.Project;
using AssetRipper.Numerics;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Extensions;
using SharpGLTF.Geometry;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using System.Numerics;

namespace AssetRipper.Export.UnityProjects.Meshes
{
	/// <summary>
	/// Exports regular meshes to GLB format (FBX alternative) while keeping skinned meshes in native format.
	/// This provides better compatibility for skinned meshes which require bone/skeleton data.
	/// </summary>
	public sealed class FbxMeshExporter : BinaryAssetExporter
	{
		private readonly YamlStreamedAssetExporter yamlExporter = new();
		private readonly GlbMeshExporter glbExporter = new();

		public override bool TryCreateCollection(IUnityObjectBase asset, TemporaryAssetCollection temporaryFile, [NotNullWhen(true)] out IExportCollection? exportCollection)
		{
			if (asset is IMesh mesh && mesh.IsSet())
			{
				// Check if mesh has skin data (bone weights)
				if (HasSkinData(mesh))
				{
					// Skinned mesh - use native YAML format for better compatibility
					exportCollection = new YamlStreamedAssetExportCollection(yamlExporter, asset);
					return true;
				}
				else
				{
					// Regular mesh - export to GLB (FBX alternative)
					exportCollection = new GlbExportCollection(this, asset);
					return true;
				}
			}
			else
			{
				exportCollection = null;
				return false;
			}
		}

		public override bool Export(IExportContainer container, IUnityObjectBase asset, string path)
		{
			IMesh mesh = (IMesh)asset;

			// Double-check: if skinned, delegate to YAML exporter
			if (HasSkinData(mesh))
			{
				return yamlExporter.Export(container, asset, path);
			}

			// Regular mesh - export as GLB
			byte[] data = ExportBinary(mesh);
			if (data.Length == 0)
			{
				return false;
			}
			File.WriteAllBytes(path, data);
			return true;
		}

		/// <summary>
		/// Checks if the mesh contains skin/bone weight data.
		/// </summary>
		private static bool HasSkinData(IMesh mesh)
		{
			try
			{
				mesh.ReadData(
					out Vector3[]? vertices,
					out Vector3[]? _,  // normals
					out Vector4[]? _,  // tangents
					out ColorFloat[]? _,  // colors
					out BoneWeight4[]? skin,
					out Vector2[]? _,  // uv0
					out Vector2[]? _,  // uv1
					out Vector2[]? _,  // uv2
					out Vector2[]? _,  // uv3
					out Vector2[]? _,  // uv4
					out Vector2[]? _,  // uv5
					out Vector2[]? _,  // uv6
					out Vector2[]? _,  // uv7
					out _,  // bindpose
					out uint[] _);  // processedIndexBuffer

				// Mesh has skin data if skin array is not null and has valid bone weights
				if (skin != null && vertices != null && skin.Length == vertices.Length)
				{
					// Additionally check if any bone weights are non-zero
					foreach (var boneWeight in skin)
					{
						if (boneWeight.Weight0 > 0 || boneWeight.Weight1 > 0 ||
						    boneWeight.Weight2 > 0 || boneWeight.Weight3 > 0)
						{
							return true;
						}
					}
				}
				return false;
			}
			catch
			{
				// If we can't read the mesh data, assume it's not skinned
				return false;
			}
		}

		private static byte[] ExportBinary(IMesh mesh)
		{
			SceneBuilder sceneBuilder = new();
			MaterialBuilder material = new MaterialBuilder("DefaultMaterial");

			AddMeshToScene(sceneBuilder, material, mesh);

			SharpGLTF.Schema2.WriteSettings writeSettings = new();

			return sceneBuilder.ToGltf2().WriteGLB(writeSettings).ToArray();
		}

		private static bool AddMeshToScene(SceneBuilder sceneBuilder, MaterialBuilder material, IMesh mesh)
		{
			if (MeshData.TryMakeFromMesh(mesh, out MeshData meshData))
			{
				NodeBuilder rootNodeForMesh = new NodeBuilder(mesh.Name);
				sceneBuilder.AddNode(rootNodeForMesh);

				(AssetRipper.SourceGenerated.Subclasses.SubMesh.ISubMesh, MaterialBuilder)[] subMeshes =
					new (AssetRipper.SourceGenerated.Subclasses.SubMesh.ISubMesh, MaterialBuilder)[1];

				for (int submeshIndex = 0; submeshIndex < meshData.Mesh.SubMeshes.Count; submeshIndex++)
				{
					var subMesh = meshData.Mesh.SubMeshes[submeshIndex];
					subMeshes[0] = (subMesh, material);

					IMeshBuilder<MaterialBuilder> subMeshBuilder = GlbSubMeshBuilder.BuildSubMeshes(
						subMeshes, meshData, Transformation.Identity, Transformation.Identity);
					NodeBuilder subMeshNode = rootNodeForMesh.CreateNode($"SubMesh_{submeshIndex}");
					sceneBuilder.AddRigidMesh(subMeshBuilder, subMeshNode);
				}
				return true;
			}
			return false;
		}
	}
}
