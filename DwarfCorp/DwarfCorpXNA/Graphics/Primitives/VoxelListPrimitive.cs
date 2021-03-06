using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DwarfCorp.GameStates;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading;
using System.Collections.Concurrent;

namespace DwarfCorp
{
    /// <summary>
    /// Represents a collection of voxels with a surface mesh. Efficiently culls away
    /// invisible voxels, and properly constructs ramps.
    /// </summary>
    public class VoxelListPrimitive : GeometricPrimitive
    {
        private static Vector3[] FaceDeltas = null;

        protected void InitializeStatics()
        {
            if(FaceDeltas == null)
            {
                FaceDeltas = new Vector3[6];
                FaceDeltas[(int)BoxFace.Back] = new Vector3(0, 0, 1);
                FaceDeltas[(int)BoxFace.Front] = new Vector3(0, 0, -1);
                FaceDeltas[(int)BoxFace.Left] = new Vector3(-1, 0, 0);
                FaceDeltas[(int)BoxFace.Right] = new Vector3(1, 0, 0);
                FaceDeltas[(int)BoxFace.Top] = new Vector3(0, 1, 0);
                FaceDeltas[(int)BoxFace.Bottom] = new Vector3(0, -1, 0);
            }
        }

        public VoxelListPrimitive() :
            base()
        {
            InitializeStatics();
        }

        public static bool IsTopVertex(VoxelVertex v)
        {
            return v == VoxelVertex.BackTopLeft || v == VoxelVertex.FrontTopLeft || v == VoxelVertex.FrontTopRight || v == VoxelVertex.BackTopRight;
        }

        protected static bool ShouldDrawFace(BoxFace face, RampType neighborRamp, RampType myRamp)
        {
            switch (face)
            {
                case BoxFace.Top:
                case BoxFace.Bottom:
                    return true;
                case BoxFace.Front:
                    return CheckRamps(myRamp, RampType.TopBackLeft, RampType.TopBackRight,
                        neighborRamp, RampType.TopFrontLeft, RampType.TopFrontRight);
                case BoxFace.Back:
                    return CheckRamps(myRamp, RampType.TopFrontLeft, RampType.TopFrontRight,
                        neighborRamp, RampType.TopBackLeft, RampType.TopBackRight);
                case BoxFace.Left:
                    return CheckRamps(myRamp, RampType.TopBackLeft, RampType.TopFrontLeft,
                        neighborRamp, RampType.TopBackRight, RampType.TopFrontRight);
                case BoxFace.Right:
                    return CheckRamps(myRamp, RampType.TopBackRight, RampType.TopFrontRight,
                        neighborRamp, RampType.TopBackLeft, RampType.TopFrontLeft);
                default:
                    return false;
            }
        }

        private static bool RampSet(RampType ToCheck, RampType For)
        {
            return (ToCheck & For) != 0;
        }

        private static bool CheckRamps(RampType A, RampType A1, RampType A2, RampType B, RampType B1, RampType B2)
        {
            return (!RampSet(A, A1) && RampSet(B, B1)) || (!RampSet(A, A2) && RampSet(B, B2));
        }

        protected static bool IsSideFace(BoxFace face)
        {
            return face != BoxFace.Top && face != BoxFace.Bottom;
        }

        protected static bool IsFaceVisible(VoxelHandle voxel, VoxelHandle neighbor, BoxFace face)
        {
            return
                !neighbor.IsValid
                || (neighbor.IsExplored && neighbor.IsEmpty)
                || (neighbor.Type.IsTransparent && !voxel.Type.IsTransparent)
                || !neighbor.IsVisible
                || (
                    neighbor.Type.CanRamp
                    && neighbor.RampType != RampType.None
                    && IsSideFace(face)
                    && ShouldDrawFace(face, neighbor.RampType, voxel.RampType)
                );
        }

        public void InitializeFromChunk(VoxelChunk chunk)
        {
            if (chunk == null)
                return;

            int[] ambientValues = new int[4];
            VertexCount = 0;
            IndexCount = 0;
            BoxPrimitive bedrockModel = VoxelLibrary.GetPrimitive("Bedrock");
            var sliceStack = new List<RawPrimitive>();
            var totalBuilt = 0;
            var lightCache = new Dictionary<GlobalVoxelCoordinate, VertexColorInfo>();
            var exploredCache = new Dictionary<GlobalVoxelCoordinate, bool>();

            for (var y = 0; y < chunk.Manager.ChunkData.MaxViewingLevel; ++y)
            {
                RawPrimitive sliceGeometry = null;

                lock (chunk.Data.SliceCache)
                {
                    var cachedSlice = chunk.Data.SliceCache[y];

                    if (chunk.Data.VoxelsPresentInSlice[y] == 0)
                    {
                        lightCache.Clear(); // If we skip a slice, nothing in the cache will be reused.
                        exploredCache.Clear();

                        if (cachedSlice != null)
                        {
                            chunk.Data.SliceCache[y] = null;
                            totalBuilt += 1;
                        }
                        continue;
                    }

                    if (cachedSlice != null)
                    {
                        lightCache.Clear(); // If we skip a slice, nothing in the cache will be reused.
                        exploredCache.Clear();

                        sliceStack.Add(cachedSlice);
                        //totalBuilt += 1;

                        if (GameSettings.Default.GrassMotes)
                            chunk.RebuildMoteLayerIfNull(y);

                        continue;
                    }

                    sliceGeometry = new RawPrimitive
                    {
                        Vertices = new ExtendedVertex[128],
                        Indexes = new ushort[128]
                    };
                    
                    chunk.Data.SliceCache[y] = sliceGeometry;
                }

                if (GameSettings.Default.CalculateRamps)
                {
                    UpdateCornerRamps(chunk, y);
                    UpdateNeighborEdgeRamps(chunk, y);
                }                    

                if (GameSettings.Default.GrassMotes)
                    chunk.RebuildMoteLayer(y);
                
                for (var x = 0; x < VoxelConstants.ChunkSizeX; ++x)
                {
                    for (var z = 0; z < VoxelConstants.ChunkSizeZ; ++z)
                    {
                        BuildVoxelGeometry(ref sliceGeometry.Vertices, ref sliceGeometry.Indexes, ref sliceGeometry.VertexCount, ref sliceGeometry.IndexCount,
                            x, y, z, chunk, bedrockModel, ambientValues, lightCache, exploredCache);
                    }
                }

                sliceStack.Add(sliceGeometry);
                totalBuilt += 1;
            }

            //if (totalBuilt > 0)
            //{
                var combinedGeometry = RawPrimitive.Concat(sliceStack);

                Vertices = combinedGeometry.Vertices;
                VertexCount = combinedGeometry.VertexCount;
                Indexes = combinedGeometry.Indexes;
                IndexCount = combinedGeometry.IndexCount;

                chunk.PrimitiveMutex.WaitOne();
                chunk.NewPrimitive = this;
                chunk.NewPrimitiveReceived = true;
                chunk.PrimitiveMutex.ReleaseMutex();
            //}
        }

        private static GlobalVoxelCoordinate GetCacheKey(VoxelHandle Handle, VoxelVertex Vertex)
        {
            var coord = Handle.Coordinate;

            if ((Vertex & VoxelVertex.Front) == VoxelVertex.Front)
                coord = new GlobalVoxelCoordinate(coord.X, coord.Y, coord.Z + 1);

            if ((Vertex & VoxelVertex.Top) == VoxelVertex.Top)
                coord = new GlobalVoxelCoordinate(coord.X, coord.Y + 1, coord.Z);

            if ((Vertex & VoxelVertex.Right) == VoxelVertex.Right)
                coord = new GlobalVoxelCoordinate(coord.X + 1, coord.Y, coord.Z);

            return coord;
        }

        private static void BuildVoxelGeometry(
            ref ExtendedVertex[] Verticies,
            ref ushort[] Indicies,
            ref int VertexCount,
            ref int IndexCount,
            int X,
            int Y,
            int Z,
            VoxelChunk Chunk,
            BoxPrimitive BedrockModel,
            int[] AmbientScratchSpace,
            Dictionary<GlobalVoxelCoordinate, VertexColorInfo> LightCache,
            Dictionary<GlobalVoxelCoordinate, bool> ExploredCache)
        {
            var v = new VoxelHandle(Chunk, new LocalVoxelCoordinate(X, Y, Z));

            if ((v.IsExplored && v.IsEmpty) || !v.IsVisible) return;

            var primitive = VoxelLibrary.GetPrimitive(v.Type);
            if (v.IsExplored && primitive == null) return;

            if (primitive == null) primitive = BedrockModel;

            var tint = v.Type.Tint;

            var uvs = primitive.UVs;

            if (v.Type.HasTransitionTextures && v.IsExplored)
                uvs = ComputeTransitionTexture(new VoxelHandle(v.Chunk.Manager.ChunkData, v.Coordinate));

            BuildVoxelTopFaceGeometry(ref Verticies, ref Indicies, ref VertexCount, ref IndexCount,
                Chunk, AmbientScratchSpace, LightCache, ExploredCache, primitive, v, tint, uvs, 0);
            for (int i = 1; i < 6; i++)
                BuildVoxelFaceGeometry(ref Verticies, ref Indicies, ref VertexCount, ref IndexCount, Chunk,
                    AmbientScratchSpace, LightCache, ExploredCache, primitive, v, tint, uvs, i);
        }

        private static void BuildVoxelFaceGeometry(
            ref ExtendedVertex[] Verticies,
            ref ushort[] Indicies,
            ref int VertexCount,
            ref int IndexCount,
            VoxelChunk Chunk,
            int[] AmbientScratchSpace,
            Dictionary<GlobalVoxelCoordinate, VertexColorInfo> LightCache,
            Dictionary<GlobalVoxelCoordinate, bool> ExploredCache,
            BoxPrimitive Primitive,
            VoxelHandle V,
            Color Tint,
            BoxPrimitive.BoxTextureCoords UVs,
            int i)
        {
                var face = (BoxFace)i;
                var delta = FaceDeltas[i];

            var faceVoxel = new VoxelHandle(Chunk.Manager.ChunkData,
                V.Coordinate + GlobalVoxelOffset.FromVector3(delta));

                if (!IsFaceVisible(V, faceVoxel, face))
                    return;

                var faceDescriptor = Primitive.GetFace(face);
                var indexOffset = VertexCount;

                for (int faceVertex = 0; faceVertex < faceDescriptor.VertexCount; faceVertex++)
                {
                    var vertex = Primitive.Vertices[faceDescriptor.VertexOffset + faceVertex];
                    var voxelVertex = Primitive.Deltas[faceDescriptor.VertexOffset + faceVertex];

                    var cacheKey = GetCacheKey(V, voxelVertex);

                    VertexColorInfo vertexColor;
                    if (!LightCache.TryGetValue(cacheKey, out vertexColor))
                    {
                        vertexColor = CalculateVertexLight(V, voxelVertex, Chunk.Manager);
                        LightCache.Add(cacheKey, vertexColor);
                    }
                    
                    AmbientScratchSpace[faceVertex] = vertexColor.AmbientColor;

                    var rampOffset = Vector3.Zero;
                    if (V.Type.CanRamp && ShouldRamp(voxelVertex, V.RampType))
                        rampOffset = new Vector3(0, -V.Type.RampSize, 0);

                    EnsureSpace(ref Verticies, VertexCount);

                    var worldPosition = V.WorldPosition + vertex.Position;

                    Verticies[VertexCount] = new ExtendedVertex(
                        worldPosition + rampOffset + VertexNoise.GetNoiseVectorFromRepeatingTexture(worldPosition),
                        vertexColor.AsColor(),
                        Tint,
                        UVs.Uvs[faceDescriptor.VertexOffset + faceVertex],
                        UVs.Bounds[faceDescriptor.IndexOffset / 6]);

                    VertexCount++;
                }

                bool flippedQuad = AmbientScratchSpace[0] + AmbientScratchSpace[2] >
                                  AmbientScratchSpace[1] + AmbientScratchSpace[3];

                for (int idx = faceDescriptor.IndexOffset; idx < faceDescriptor.IndexCount +
                    faceDescriptor.IndexOffset; idx++)
                {
                    EnsureSpace(ref Indicies, IndexCount);

                    ushort offset = flippedQuad ? Primitive.FlippedIndexes[idx] : Primitive.Indexes[idx];
                    ushort offset0 = flippedQuad ? Primitive.FlippedIndexes[faceDescriptor.IndexOffset] : Primitive.Indexes[faceDescriptor.IndexOffset];
                    Indicies[IndexCount] = (ushort)(indexOffset + offset - offset0);
                    IndexCount++;
                }
        }

        private static void BuildVoxelTopFaceGeometry(
    ref ExtendedVertex[] Verticies,
    ref ushort[] Indicies,
    ref int VertexCount,
    ref int IndexCount,
    VoxelChunk Chunk,
    int[] AmbientScratchSpace,
    Dictionary<GlobalVoxelCoordinate, VertexColorInfo> LightCache,
    Dictionary<GlobalVoxelCoordinate, bool> ExploredCache,
    BoxPrimitive Primitive,
    VoxelHandle V,
    Color Tint,
    BoxPrimitive.BoxTextureCoords UVs,
    int i)
        {
            var face = (BoxFace)i;
            var delta = FaceDeltas[i];

            var faceVoxel = new VoxelHandle(Chunk.Manager.ChunkData,
                V.Coordinate + GlobalVoxelOffset.FromVector3(delta));

            if (!IsFaceVisible(V, faceVoxel, face))
                return;

            var faceDescriptor = Primitive.GetFace(face);
            var indexOffset = VertexCount;

            int exploredVerts = 0;

            if (V.IsExplored)
                exploredVerts = 4;
            else
            {
                for (int faceVertex = 0; faceVertex < faceDescriptor.VertexCount; ++faceVertex)
                {
                    var voxelVertex = Primitive.Deltas[faceDescriptor.VertexOffset + faceVertex];
                    var cacheKey = GetCacheKey(V, voxelVertex);
                    bool anyNeighborExplored = true;

                    if (!ExploredCache.TryGetValue(cacheKey, out anyNeighborExplored))
                    {
                        anyNeighborExplored = VoxelHelpers.EnumerateVertexNeighbors2D(V.Coordinate, voxelVertex)
                            .Select(c => new VoxelHandle(V.Chunk.Manager.ChunkData, c))
                            .Any(n => n.IsValid && n.IsExplored);
                        ExploredCache.Add(cacheKey, anyNeighborExplored);
                    }

                    if (anyNeighborExplored)
                        exploredVerts += 1;
                }
            }

            if (exploredVerts != 0)
            {
                for (int faceVertex = 0; faceVertex < faceDescriptor.VertexCount; faceVertex++)
                {
                    var vertex = Primitive.Vertices[faceDescriptor.VertexOffset + faceVertex];
                    var voxelVertex = Primitive.Deltas[faceDescriptor.VertexOffset + faceVertex];

                    var cacheKey = GetCacheKey(V, voxelVertex);
                    var vertexTint = Tint;

                    if (exploredVerts != 4)
                    {
                        bool anyNeighborExplored = true;

                        if (!ExploredCache.TryGetValue(cacheKey, out anyNeighborExplored))
                        {
                    //        anyNeighborExplored = VoxelHelpers.EnumerateVertexNeighbors2D(V.Coordinate, voxelVertex)
                    //          .Select(c => new VoxelHandle(V.Chunk.Manager.ChunkData, c))
                    //          .Any(n => n.IsValid && n.IsExplored);
                    //        ExploredCache.Add(cacheKey, anyNeighborExplored);
                        }

                        if (!anyNeighborExplored) vertexTint = new Color(0, 0, 0, 255);
                    }

                    VertexColorInfo vertexColor;
                    if (!LightCache.TryGetValue(cacheKey, out vertexColor))
                    {
                        vertexColor = CalculateVertexLight(V, voxelVertex, Chunk.Manager);
                        LightCache.Add(cacheKey, vertexColor);
                    }

                    AmbientScratchSpace[faceVertex] = vertexColor.AmbientColor;

                    var rampOffset = Vector3.Zero;
                    if (V.Type.CanRamp && ShouldRamp(voxelVertex, V.RampType))
                        rampOffset = new Vector3(0, -V.Type.RampSize, 0);

                    EnsureSpace(ref Verticies, VertexCount);

                    var worldPosition = V.WorldPosition + vertex.Position;

                    Verticies[VertexCount] = new ExtendedVertex(
                        worldPosition + rampOffset + VertexNoise.GetNoiseVectorFromRepeatingTexture(worldPosition),
                        vertexColor.AsColor(),
                        vertexTint,
                        UVs.Uvs[faceDescriptor.VertexOffset + faceVertex],
                        UVs.Bounds[faceDescriptor.IndexOffset / 6]);

                    VertexCount++;
                }

                bool flippedQuad = AmbientScratchSpace[0] + AmbientScratchSpace[2] >
                                  AmbientScratchSpace[1] + AmbientScratchSpace[3];

                for (int idx = faceDescriptor.IndexOffset; idx < faceDescriptor.IndexCount +
                    faceDescriptor.IndexOffset; idx++)
                {
                    EnsureSpace(ref Indicies, IndexCount);

                    ushort offset = flippedQuad ? Primitive.FlippedIndexes[idx] : Primitive.Indexes[idx];
                    ushort offset0 = flippedQuad ? Primitive.FlippedIndexes[faceDescriptor.IndexOffset] : Primitive.Indexes[faceDescriptor.IndexOffset];
                    Indicies[IndexCount] = (ushort)(indexOffset + offset - offset0);
                    IndexCount++;
                }
            }
            else
            {
                indexOffset = VertexCount;

                for (int faceVertex = 0; faceVertex < faceDescriptor.VertexCount; faceVertex++)
                {
                    var vertex = Primitive.Vertices[faceDescriptor.VertexOffset + faceVertex];
                    var voxelVertex = Primitive.Deltas[faceDescriptor.VertexOffset + faceVertex];

                    var rampOffset = Vector3.Zero;
                    if (V.Type.CanRamp && ShouldRamp(voxelVertex, V.RampType))
                        rampOffset = new Vector3(0, -V.Type.RampSize, 0);

                    EnsureSpace(ref Verticies, VertexCount);

                    var worldPosition = V.WorldPosition + vertex.Position;

                    Verticies[VertexCount] = new ExtendedVertex(
                        worldPosition + rampOffset + VertexNoise.GetNoiseVectorFromRepeatingTexture(worldPosition),
                        new Color(0,0,0,255),
                        new Color(0,0,0,255),
                        new Vector2(12.5f / 16.0f, 0.5f / 16.0f),
                        // xy - min, zw - max
                        new Vector4(12.0f / 16.0f, 0.0f, 13.0f / 16.0f, 1.0f / 16.0f));

                    VertexCount++;
                }

                for (int idx = faceDescriptor.IndexOffset; idx < faceDescriptor.IndexCount +
                    faceDescriptor.IndexOffset; idx++)
                {
                    EnsureSpace(ref Indicies, IndexCount);

                    ushort offset = Primitive.Indexes[idx];
                    ushort offset0 = Primitive.Indexes[faceDescriptor.IndexOffset];
                    Indicies[IndexCount] = (ushort)(indexOffset + offset - offset0);
                    IndexCount++;
                }
            }
        }

        private static bool ShouldRamp(VoxelVertex vertex, RampType rampType)
        {
            bool toReturn = false;

            if ((rampType & RampType.TopFrontRight) == RampType.TopFrontRight)
                toReturn = (vertex == VoxelVertex.FrontTopRight);

            if ((rampType & RampType.TopBackRight) == RampType.TopBackRight)
                toReturn = toReturn || (vertex == VoxelVertex.BackTopRight);

            if ((rampType & RampType.TopFrontLeft) == RampType.TopFrontLeft)
                toReturn = toReturn || (vertex == VoxelVertex.FrontTopLeft);

            if ((rampType & RampType.TopBackLeft) == RampType.TopBackLeft)
                toReturn = toReturn || (vertex == VoxelVertex.BackTopLeft);

            return toReturn;
        }

        private static VoxelVertex[] TopVerticies = new VoxelVertex[]
            {
                VoxelVertex.FrontTopLeft,
                VoxelVertex.FrontTopRight,
                VoxelVertex.BackTopLeft,
                VoxelVertex.BackTopRight
            };
        
        private static void UpdateVoxelRamps(VoxelHandle V)
        {
            if (V.IsEmpty || !V.IsVisible || !V.Type.CanRamp)
            {
                V.RampType = RampType.None;
                return;
            }

            if (V.Coordinate.Y < VoxelConstants.ChunkSizeY - 1)
            {
                var lCoord = V.Coordinate.GetLocalVoxelCoordinate();
                var vAbove = new VoxelHandle(V.Chunk, new LocalVoxelCoordinate(lCoord.X, lCoord.Y + 1, lCoord.Z));
                if (!vAbove.IsEmpty)
                {
                    V.RampType = RampType.None;
                    return;
                }
            }

            var compositeRamp = RampType.None;

            foreach (var vertex in TopVerticies)
            {
                // If there are no empty neighbors, no slope.
                if (!VoxelHelpers.EnumerateVertexNeighbors2D(V.Coordinate, vertex)
                    .Any(n =>
                    {
                        var handle = new VoxelHandle(V.Chunk.Manager.ChunkData, n);
                        return !handle.IsValid || handle.IsEmpty;
                    }))
                    continue;

                switch (vertex)
                {
                    case VoxelVertex.FrontTopLeft:
                        compositeRamp |= RampType.TopFrontLeft;
                        break;
                    case VoxelVertex.FrontTopRight:
                        compositeRamp |= RampType.TopFrontRight;
                        break;
                    case VoxelVertex.BackTopLeft:
                        compositeRamp |= RampType.TopBackLeft;
                        break;
                    case VoxelVertex.BackTopRight:
                        compositeRamp |= RampType.TopBackRight;
                        break;
                }
            }

            V.RampType = compositeRamp;
        }

        public static void UpdateCornerRamps(VoxelChunk Chunk, int Y)
        {
            for (int x = 0; x < VoxelConstants.ChunkSizeX; x++)
                for (int z = 0; z < VoxelConstants.ChunkSizeZ; z++)
                    UpdateVoxelRamps(new VoxelHandle(Chunk, new LocalVoxelCoordinate(x, Y, z)));               
        }

        private static void UpdateNeighborEdgeRamps(VoxelChunk Chunk, int Y)
        {
            var startChunkCorner = new GlobalVoxelCoordinate(Chunk.ID, new LocalVoxelCoordinate(0, 0, 0))
                + new GlobalVoxelOffset(-1, 0, -1);
            var endChunkCorner = new GlobalVoxelCoordinate(Chunk.ID, new LocalVoxelCoordinate(0, 0, 0))
                + new GlobalVoxelOffset(VoxelConstants.ChunkSizeX, 0, VoxelConstants.ChunkSizeZ);

            for (int x = startChunkCorner.X; x <= endChunkCorner.X; ++x)
            {
                var v1 = new VoxelHandle(Chunk.Manager.ChunkData,
                    new GlobalVoxelCoordinate(x, Y, startChunkCorner.Z));
                if (v1.IsValid) UpdateVoxelRamps(v1);

                var v2 = new VoxelHandle(Chunk.Manager.ChunkData,
                    new GlobalVoxelCoordinate(x, Y, endChunkCorner.Z));
                if (v2.IsValid) UpdateVoxelRamps(v2);
            }

            for (int z = startChunkCorner.Z + 1; z < endChunkCorner.Z; ++z)
            {
                var v1 = new VoxelHandle(Chunk.Manager.ChunkData,
                    new GlobalVoxelCoordinate(startChunkCorner.X, Y, z));
                if (v1.IsValid) UpdateVoxelRamps(v1);

                var v2 = new VoxelHandle(Chunk.Manager.ChunkData,
                    new GlobalVoxelCoordinate(endChunkCorner.X, Y, z));
                if (v2.IsValid) UpdateVoxelRamps(v2);
            }
        }

        private static void EnsureSpace<T>(ref T[] In, int Size)
        {
            if (Size >= In.Length)
            {
                var r = new T[In.Length * 2];
                In.CopyTo(r, 0);
                In = r;
            }
        }

        private struct VertexColorInfo
        {
            public int SunColor;
            public int AmbientColor;
            public int DynamicColor;

            public Color AsColor()
            {
                return new Color(SunColor, AmbientColor, DynamicColor);
            }
        }

        private static VertexColorInfo CalculateVertexLight(VoxelHandle Vox, VoxelVertex Vertex,
            ChunkManager chunks)
        {
            int neighborsEmpty = 0;
            int neighborsChecked = 0;

            var color = new VertexColorInfo();
            color.DynamicColor = 0;
            color.SunColor = 0;

            foreach (var c in VoxelHelpers.EnumerateVertexNeighbors(Vox.Coordinate, Vertex))
            {
                var v = new VoxelHandle(chunks.ChunkData, c);
                if (!v.IsValid) continue;

                color.SunColor += v.SunColor;
                if (!v.IsEmpty || !v.IsExplored)
                {
                    if (v.Type.EmitsLight) color.DynamicColor = 255;
                    neighborsEmpty += 1;
                    neighborsChecked += 1;
                }
                else
                    neighborsChecked += 1;
            }

            float proportionHit = (float)neighborsEmpty / (float)neighborsChecked;
            color.AmbientColor = (int)Math.Min((1.0f - proportionHit) * 255.0f, 255);
            color.SunColor = (int)Math.Min((float)color.SunColor / (float)neighborsChecked, 255);

            return color;
        }

        private static BoxPrimitive.BoxTextureCoords ComputeTransitionTexture(VoxelHandle V)
        {
            var type = V.Type;
            var primitive = VoxelLibrary.GetPrimitive(type);

            if (!type.HasTransitionTextures && primitive != null)
                return primitive.UVs;
            else if (primitive == null)
                return null;
            else
            {
                var transition = ComputeTransitions(V.Chunk.Manager.ChunkData, V, type);
                return type.TransitionTextures[transition];
            }
        }

        private static BoxTransition ComputeTransitions(
            ChunkData Data,
            VoxelHandle V,
            VoxelType Type)
        {
            if (Type.Transitions == VoxelType.TransitionType.Horizontal)
            {
                var value = ComputeTransitionValueOnPlane(
                    VoxelHelpers.EnumerateManhattanNeighbors2D(V.Coordinate)
                    .Select(c => new VoxelHandle(Data, c)), Type);

                return new BoxTransition()
                {
                    Top = (TransitionTexture)value
                };
            }
            else
            {
                var transitionFrontBack = ComputeTransitionValueOnPlane(
                    VoxelHelpers.EnumerateManhattanNeighbors2D(V.Coordinate, ChunkManager.SliceMode.Z)
                    .Select(c => new VoxelHandle(Data, c)),
                    Type);

                var transitionLeftRight = ComputeTransitionValueOnPlane(
                    VoxelHelpers.EnumerateManhattanNeighbors2D(V.Coordinate, ChunkManager.SliceMode.X)
                    .Select(c => new VoxelHandle(Data, c)),
                    Type);

                return new BoxTransition()
                {
                    Front = (TransitionTexture)transitionFrontBack,
                    Back = (TransitionTexture)transitionFrontBack,
                    Left = (TransitionTexture)transitionLeftRight,
                    Right = (TransitionTexture)transitionLeftRight
                };
            }
        }

        // Todo: Reorder 2d neighbors to make this unecessary.
        private static int[] TransitionMultipliers = new int[] { 2, 8, 4, 1 };

        private static int ComputeTransitionValueOnPlane(IEnumerable<VoxelHandle> Neighbors, VoxelType Type)
        {
            int index = 0;
            int accumulator = 0;
            foreach (var v in Neighbors)
            {
                if (v.IsValid && !v.IsEmpty && v.Type == Type)
                    accumulator += TransitionMultipliers[index];
                index += 1;
            }
            return accumulator;
        }
    }
}