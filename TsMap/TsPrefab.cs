using System;
using System.Collections.Generic;
using System.Linq;

namespace TsMap
{
    public struct TsPrefabNode
    {
        public float X;
        public float Z;
        public float RotX;
        public float RotZ;
    }

    public struct TsMapPoint
    {
        public float X;
        public float Z;
        public int LaneOffset;
        public int LaneCount;
        public bool Hidden;
        public byte PrefabColorFlags;
        public int NeighbourCount;
        public List<int> Neighbours;
    }

    public class TsSpawnPoint
    {
        public float X;
        public float Z;
        public TsSpawnPointType Type;
    }

    public class TsTriggerPoint
    {
        public uint TriggerId;
        public ulong TriggerActionUid;
        public float X;
        public float Z;
    }

    public class TsPrefab
    {
        private const int NodeBlockSize = 0x68;
        private const int MapPointBlockSize = 0x30;
        private const int SpawnPointBlockSize = 0x20;
        private const int TriggerPointBlockSize = 0x30;

        private readonly string _filePath;
        public ulong Token { get; }
        public string Category { get; }

        private byte[] _stream;

        public List<TsPrefabNode> PrefabNodes { get; private set; }
        public List<TsSpawnPoint> SpawnPoints { get; private set; }
        public List<TsMapPoint> MapPoints { get; private set; }
        public List<TsTriggerPoint> TriggerPoints { get; private set; }

        public TsPrefab(TsMapper mapper, string filePath, ulong token, string category)
        {
            _filePath = filePath;
            Token = token;
            Category = category;

            var file = mapper.Rfs.GetFileEntry(_filePath);

            if (file == null) return;

            _stream = file.Entry.Read();

            Parse();
        }

        private void Parse()
        {
            PrefabNodes = new List<TsPrefabNode>();
            SpawnPoints = new List<TsSpawnPoint>();
            MapPoints = new List<TsMapPoint>();
            TriggerPoints = new List<TsTriggerPoint>();

            var fileOffset = 0x0;

            var version = MemoryHelper.ReadInt32(_stream, fileOffset);

            var nodeCount = MemoryHelper.ReadInt32(_stream, fileOffset += 0x04);
            var spawnPointCount = MemoryHelper.ReadInt32(_stream, fileOffset += 0x10);
            var mapPointCount = MemoryHelper.ReadInt32(_stream, fileOffset += 0x0C);
            var triggerPointCount = MemoryHelper.ReadInt32(_stream, fileOffset += 0x04);

            if (version > 0x15) fileOffset += 0x04; // http://modding.scssoft.com/wiki/Games/ETS2/Modding_guides/1.30#Prefabs

            var nodeOffset = MemoryHelper.ReadInt32(_stream, fileOffset += 0x08);
            var spawnPointOffset = MemoryHelper.ReadInt32(_stream, fileOffset += 0x10);
            var mapPointOffset = MemoryHelper.ReadInt32(_stream, fileOffset += 0x10);
            var triggerPointOffset = MemoryHelper.ReadInt32(_stream, fileOffset += 0x04);

            for (var i = 0; i < nodeCount; i++)
            {
                var nodeBaseOffset = nodeOffset + (i * NodeBlockSize);
                var node = new TsPrefabNode
                {
                    X = MemoryHelper.ReadSingle(_stream, nodeBaseOffset + 0x10),
                    Z = MemoryHelper.ReadSingle(_stream, nodeBaseOffset + 0x18),
                    RotX = MemoryHelper.ReadSingle(_stream, nodeBaseOffset + 0x1C),
                    RotZ = MemoryHelper.ReadSingle(_stream, nodeBaseOffset + 0x24),
                };
                PrefabNodes.Add(node);
            }

            for (var i = 0; i < spawnPointCount; i++)
            {
                var spawnPointBaseOffset = spawnPointOffset + (i * SpawnPointBlockSize);
                var spawnPoint = new TsSpawnPoint
                {
                    X = MemoryHelper.ReadSingle(_stream, spawnPointBaseOffset),
                    Z = MemoryHelper.ReadSingle(_stream, spawnPointBaseOffset + 0x08),
                    Type = (TsSpawnPointType)MemoryHelper.ReadUInt32(_stream, spawnPointBaseOffset + 0x1C)
                };
                var pointInVicinity = SpawnPoints.FirstOrDefault(point => // check if any other spawn points with the same type are close
                    point.Type == spawnPoint.Type &&
                    ((spawnPoint.X > point.X - 4 && spawnPoint.X < point.X + 4) ||
                    (spawnPoint.Z > point.Z - 4 && spawnPoint.Z < point.Z + 4)));
                if (pointInVicinity == null) SpawnPoints.Add(spawnPoint);
                // Log.Msg($"Spawn point of type: {spawnPoint.Type} in {_filePath}");
            }

            for (var i = 0; i < mapPointCount; i++)
            {
                var mapPointBaseOffset = mapPointOffset + (i * MapPointBlockSize);
                var roadLookFlags = MemoryHelper.ReadUint8(_stream, mapPointBaseOffset + 0x01);
                var laneTypeFlags = (byte) (roadLookFlags & 0x0F);
                var laneOffsetFlags = (byte)(roadLookFlags >> 4);
                int laneOffset;
                switch (laneOffsetFlags)
                {
                    case 1: laneOffset = 1; break;
                    case 2: laneOffset = 2; break;
                    case 3: laneOffset = 5; break;
                    case 4: laneOffset = 10; break;
                    case 5: laneOffset = 15; break;
                    case 6: laneOffset = 20; break;
                    case 7: laneOffset = 25; break;
                    default: laneOffset = 0; break;

                }
                int laneCount;
                switch (laneTypeFlags) // TODO: Change these (not really used atm)
                {
                    case 0: laneCount = 1; break;
                    case 1: laneCount = 2; break;
                    case 2: laneCount = 4; break;
                    case 3: laneCount = 6; break;
                    case 4: laneCount = 8; break;
                    case 5: laneCount = 5; break;
                    case 6: laneCount = 7; break;
                    case 8: laneCount = 3; break;
                    case 13: laneCount = -1; break;
                    case 14: laneCount = 2; break; // auto ; temp set at 2
                    default:
                        laneCount = 1;
                        // Log.Msg($"Unknown LaneType: {laneTypeFlags}");
                        break;
                }

                var prefabColorFlags = MemoryHelper.ReadUint8(_stream, mapPointBaseOffset + 0x02);

                var navFlags = MemoryHelper.ReadUint8(_stream, mapPointBaseOffset + 0x05);
                var hidden = (navFlags & 0x02) != 0; // Map Point is Control Node

                var point = new TsMapPoint
                {
                    LaneCount = laneCount,
                    LaneOffset = laneOffset,
                    Hidden = hidden,
                    PrefabColorFlags = prefabColorFlags,
                    X = MemoryHelper.ReadSingle(_stream, mapPointBaseOffset + 0x08),
                    Z = MemoryHelper.ReadSingle(_stream, mapPointBaseOffset + 0x10),
                    Neighbours = new List<int>(),
                    NeighbourCount = MemoryHelper.ReadInt32(_stream, mapPointBaseOffset + 0x14 + (0x04 * 6))
                };

                for (var x = 0; x < point.NeighbourCount; x++)
                {
                    point.Neighbours.Add(MemoryHelper.ReadInt32(_stream, mapPointBaseOffset + 0x14 + (x * 0x04)));
                }

                MapPoints.Add(point);
            }

            for (var i = 0; i < triggerPointCount; i++)
            {
                var triggerPointBaseOffset = triggerPointOffset + (i * TriggerPointBlockSize);
                var triggerPoint = new TsTriggerPoint
                {
                    TriggerId = MemoryHelper.ReadUInt32(_stream, triggerPointBaseOffset),
                    TriggerActionUid = MemoryHelper.ReadUInt64(_stream, triggerPointBaseOffset + 0x04),
                    X = MemoryHelper.ReadSingle(_stream, triggerPointBaseOffset + 0x1C),
                    Z = MemoryHelper.ReadSingle(_stream, triggerPointBaseOffset + 0x24),
                };
                var pointInVicinity = TriggerPoints.FirstOrDefault(point => // check if any other trigger points with the same id are close
                    point.TriggerActionUid == triggerPoint.TriggerActionUid &&
                    ((triggerPoint.X > point.X - 20 && triggerPoint.X < point.X + 20) ||
                    (triggerPoint.Z > point.Z - 20 && triggerPoint.Z < point.Z + 20)));
                if (pointInVicinity == null) TriggerPoints.Add(triggerPoint);
            }

            _stream = null;

        }
    }
}
