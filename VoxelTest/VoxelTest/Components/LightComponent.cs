﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;

namespace DwarfCorp
{

    internal class LightComponent : LocatableComponent
    {
        public byte Intensity { get; set; }
        public byte Range { get; set; }
        public DynamicLight Light { get; set; }


        public LightComponent(ComponentManager manager, string name, GameComponent parent, Matrix localTransform, Vector3 boundingBoxExtents, Vector3 boundingBoxPos, byte intensity, byte range) :
            base(manager, name, parent, localTransform, boundingBoxExtents, boundingBoxPos)
        {
            Intensity = intensity;
            Range = range;
            Light = null;
        }

        public void UpdateLight(ChunkManager chunks)
        {
            if(Light == null)
            {
                Light = chunks.GetVoxelChunkAtWorldLocation(GlobalTransform.Translation).AddLight(GlobalTransform.Translation, Range, Intensity);
            }
            else
            {
                Light.Voxel = chunks.GetVoxelReferencesAtWorldLocation(GlobalTransform.Translation)[0];
                chunks.ChunkMap[Light.Voxel.ChunkID].ShouldRebuild = true;
                chunks.ChunkMap[Light.Voxel.ChunkID].ShouldRecalculateLighting = true;
            }
        }

        public override void Update(GameTime gameTime, ChunkManager chunks, Camera camera)
        {
            if(HasMoved || Light == null)
            {
                UpdateLight(chunks);
            }


            base.Update(gameTime, chunks, camera);
        }

        public override void Die()
        {
            Light.Destroy();
            base.Die();
        }
    }

}