using RimWorld.Planet;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity.GameClasses
{
    public class WorldLayer_Color : WorldLayer
    {
        private const int SubdivisionsCount = 4;
        public const float GlowRadius = 8f;

        // ОПТИМІЗАЦІЯ: кеш згенерованої геометрії атмосфери планети (усуває розрахунки при кожному зумі/обертанні)
        private static List<Vector3> cachedVerts;
        private static List<int> cachedIndices;
        private static readonly object cacheLock = new object();

        private static void EnsureMeshGenerated()
        {
            if (cachedVerts != null) return;

            lock (cacheLock)
            {
                if (cachedVerts != null) return;
                SphereGenerator.Generate(SubdivisionsCount, 108.1f, Vector3.forward, 360f, out var verts, out var indices);
                cachedVerts = verts;
                cachedIndices = indices;
            }
        }

        public override IEnumerable Regenerate()
        {
            foreach (var item in base.Regenerate())
            {
                yield return item;
            }

            EnsureMeshGenerated();

            LayerSubMesh subMesh = GetSubMesh(WorldMaterials.PlanetGlow);
            subMesh.verts.AddRange(cachedVerts);
            subMesh.tris.AddRange(cachedIndices);
            FinalizeMesh(MeshParts.All);
        }
    }
}