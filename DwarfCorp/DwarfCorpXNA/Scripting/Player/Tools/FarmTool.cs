// BuildTool.cs
// 
//  Modified MIT License (MIT)
//  
//  Copyright (c) 2015 Completely Fair Games Ltd.
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// The following content pieces are considered PROPRIETARY and may not be used
// in any derivative works, commercial or non commercial, without explicit 
// written permission from Completely Fair Games:
// 
// * Images (sprites, textures, etc.)
// * 3D Models
// * Sound Effects
// * Music
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;

namespace DwarfCorp
{
    public class FarmTool : PlayerTool
    {
        public string PlantType { get; set; }
        public List<ResourceAmount> RequiredResources { get; set; } 
        public enum FarmMode
        {
            Tilling,
            Planting,
            Harvesting,
            WranglingAnimals
        }

        public FarmMode Mode { get; set; }

        public bool HasTile(VoxelHandle vox)
        {
            return Player.Faction.FarmTiles.Any(f => f.Vox == vox);
        }


        public bool HasPlant(VoxelHandle vox)
        {
            return HasTile(vox) && Player.Faction.FarmTiles.Any(f => f.Vox.Equals(vox) && f.PlantExists());
        }

        public bool IsBeingWorked(VoxelHandle vox)
        {
            return HasTile(vox) && Player.Faction.FarmTiles.Any(f => f.Vox.Equals(vox) && f.Farmer != null);
        }

        public override void OnVoxelsDragged(List<VoxelHandle> voxels, InputManager.MouseButton button)
        {
            switch (Mode)
            {
                case FarmMode.Planting:
                     int currentAmount =
                        Player.Faction.ListResources()
                        .Sum(resource => resource.Key == PlantType && resource.Value.NumResources > 0 ? resource.Value.NumResources : 0);
                    foreach (var voxel in voxels)
                    {

                        if (currentAmount == 0)
                        {
                            Player.World.ShowToolPopup("Not enough " + PlantType + " in stocks!");
                            break;
                        }


                        ValidatePlanting(voxel);
                    }
                    break;
                case FarmMode.Tilling:
                    foreach (var voxel in voxels)
                    {
                        ValidateTilling(voxel);
                    }
                    break;
                default:
                    break;
            }
        }

        private bool ValidatePlanting(VoxelHandle voxel)
        {
            if (voxel.Type.Name != "TilledSoil")
            {
                Player.World.ShowToolPopup("Can only plant on tilled soil!");
                return false;
            }

            if (ResourceLibrary.Resources[PlantType].Tags.Contains(Resource.ResourceTags.AboveGroundPlant))
            {
                if (voxel.SunColor == 0)
                {
                    Player.World.ShowToolPopup("Can only plant " + PlantType + " above ground.");
                    return false;
                }
            }
            else if (
                ResourceLibrary.Resources[PlantType].Tags.Contains(
                    Resource.ResourceTags.BelowGroundPlant))
            {
                if (voxel.SunColor > 0)
                {
                    Player.World.ShowToolPopup("Can only plant " + PlantType + " below ground.");
                    return false;
                }
            }

            if (HasPlant(voxel))
            {
                Player.World.ShowToolPopup("Something is already planted here!");
                return false;
            }

            var above = VoxelHelpers.GetVoxelAbove(voxel);
            if (above.IsValid && !above.IsEmpty)
            {
                Player.World.ShowToolPopup("Something is blocking the top of this tile.");
                return false;
            }

            if (IsBeingWorked(voxel))
            {
                Player.World.ShowToolPopup("This tile is already being worked.");
                return false;
            }

            Player.World.ShowToolPopup("Click to plant.");

            return true;
        }


        private bool ValidateTilling(VoxelHandle voxel)
        {
            if (!voxel.Type.IsSoil)
            {
                Player.World.ShowToolPopup(String.Format("Can only till soil (not {0})!", voxel.Type.Name));
                return false;
            }
            if (voxel.Type.Name == "TilledSoil")
            {
                Player.World.ShowToolPopup("Soil already tilled!");
                return false;
            }
            var above = VoxelHelpers.GetVoxelAbove(voxel);
            if (above.IsValid && !above.IsEmpty)
            {
                Player.World.ShowToolPopup("Something is blocking the top of this tile.");
                return false;
            }
            Player.World.ShowToolPopup("Click to till.");
            return true;
        }

        public override void OnVoxelsSelected(List<VoxelHandle> voxels, InputManager.MouseButton button)
        {
            List<CreatureAI> minions = Player.World.Master.SelectedMinions.Where(minion => minion.Stats.CurrentClass.HasAction(GameMaster.ToolMode.Farm)).ToList();
            List<FarmTask> goals = new List<FarmTask>();
            switch (Mode)
            {
                case FarmMode.Tilling:
                    foreach (var voxel in voxels)
                    {
                        if (button == InputManager.MouseButton.Left)
                        {
                            if (!ValidateTilling(voxel))
                                continue;
                            if (!HasTile(voxel))
                            {
                                FarmTile tile = new FarmTile() {Vox = voxel};
                                goals.Add(new FarmTask(tile) {Mode = FarmAct.FarmMode.Till, Plant = PlantType});
                                Player.Faction.FarmTiles.Add(tile);
                            }
                            else
                            {
                                goals.Add(new FarmTask(Player.Faction.FarmTiles.Find(tile => tile.Vox.Equals(voxel)))
                                {
                                    Mode = FarmAct.FarmMode.Till,
                                    Plant = PlantType
                                });
                            }
                        }
                        else
                        {
                            if (!HasTile(voxel) || HasPlant(voxel)) continue;
                            Drawer3D.UnHighlightVoxel(voxel);
                            foreach (FarmTile tile in Player.Faction.FarmTiles)
                            {
                                if (tile.Vox.Equals(voxel))
                                { 
                                    tile.IsCanceled = true;
                                    tile.Farmer = null;
                                }
                            }
                            Player.Faction.FarmTiles.RemoveAll(tile => tile.Vox.Equals(voxel));
                        }
                    }
                    TaskManager.AssignTasksGreedy(goals.Cast<Task>().ToList(), minions, 1);

                    foreach (CreatureAI creature in minions)
                    {
                        creature.Creature.NoiseMaker.MakeNoise("Ok", creature.Position);
                    }

                    break;
                case FarmMode.Planting:
                    int currentAmount =
                        Player.Faction.ListResources()
                        .Sum(resource => resource.Key == PlantType && resource.Value.NumResources > 0 ? resource.Value.NumResources : 0);
                    foreach (var voxel in voxels)
                    {
                        if (currentAmount == 0)
                        {
                            Player.World.ShowToolPopup("Not enough " + PlantType + " in stocks!");
                            break;
                        }                      

                        if (ValidatePlanting(voxel))
                        {
                            FarmTile tile = new FarmTile() { Vox = voxel };
                            goals.Add(new FarmTask(tile) {  Mode = FarmAct.FarmMode.Plant, Plant = PlantType, RequiredResources = RequiredResources});
                            Player.Faction.FarmTiles.Add(tile);
                            currentAmount--;
                        }
                    }
                    TaskManager.AssignTasksGreedy(goals.Cast<Task>().ToList(), minions, 1);


                    if (Player.World.Paused)
                    {
                        // Horrible hack to make it work when game is paused. Farmer doesn't get assigned until
                        // next update!
                        if (minions.Count > 0)
                        {
                            foreach (var goal in goals)
                            {
                                goal.FarmToWork.Farmer = minions[0];
                            }
                        }
                    }
                    OnConfirm(minions);
                    break;
            }
        }


        public override void OnBodiesSelected(List<Body> bodies, InputManager.MouseButton button)
        {
            switch (Mode)
            {
                case FarmMode.Harvesting:
                    {
                        List<Task> tasks = new List<Task>();
                        foreach (Body tree in bodies.Where(c => c.Tags.Contains("Vegetation")))
                        {
                            if (!tree.IsVisible || tree.IsAboveCullPlane(Player.World.ChunkManager)) continue;

                            switch (button)
                            {
                                case InputManager.MouseButton.Left:
                                    if (Player.Faction.AddEntityDesignation(tree, DesignationType.Chop) == Faction.AddEntityDesignationResult.Added)
                                    {
                                        tasks.Add(new KillEntityTask(tree, KillEntityTask.KillType.Chop) { Priority = Task.PriorityType.Low });
                                        this.Player.World.ShowToolPopup("Will harvest this " + tree.Name);
                                    }
                                    break;
                                case InputManager.MouseButton.Right:
                                    if (Player.Faction.RemoveEntityDesignation(tree, DesignationType.Chop) == Faction.RemoveEntityDesignationResult.Removed)
                                        this.Player.World.ShowToolPopup("Harvest cancelled " + tree.Name);
                                    break;
                            }
                        }
                        if (tasks.Count > 0 && Player.SelectedMinions.Count > 0)
                        {
                            TaskManager.AssignTasks(tasks, Player.SelectedMinions);
                            OnConfirm(Player.SelectedMinions);
                        }
                    }
                    break;
                case FarmMode.WranglingAnimals:
                    {
                        List<Task> tasks = new List<Task>();
                        foreach (Body animal in bodies.Where(c => c.Tags.Contains("DomesticAnimal")))
                        {
                            Drawer3D.DrawBox(animal.BoundingBox, Color.Tomato, 0.1f, false);
                            switch (button)
                            {
                                case InputManager.MouseButton.Left:
                                    {
                                        var pens = Player.Faction.GetRooms().Where(room => room is AnimalPen).Cast<AnimalPen>().Where(pen => pen.IsBuilt && 
                                        (pen.Species == "" || pen.Species == animal.GetRoot().GetComponent<Creature>().Species));

                                        if (pens.Any())
                                        {
                                            Player.Faction.AddEntityDesignation(animal, DesignationType.Wrangle);
                                            tasks.Add(new WrangleAnimalTask(animal.GetRoot().GetComponent<Creature>()));
                                            this.Player.World.ShowToolPopup("Will wrangle this " +
                                                                            animal.GetRoot().GetComponent<Creature>().Species);
                                        }
                                        else
                                        {
                                            this.Player.World.ShowToolPopup("Can't wrangle this " +
                                                                            animal.GetRoot().GetComponent<Creature>().Species +
                                                                            " : need more animal pens.");
                                        }
                                    }
                                    break;
                                case InputManager.MouseButton.Right:
                                    if (Player.Faction.RemoveEntityDesignation(animal, DesignationType.Wrangle) == Faction.RemoveEntityDesignationResult.Removed)
                                        this.Player.World.ShowToolPopup("Wrangle cancelled for " + animal.GetRoot().GetComponent<Creature>().Species);
                                    break;
                            }
                        }
                        if (tasks.Count > 0 && Player.SelectedMinions.Count > 0)
                        {
                            TaskManager.AssignTasks(tasks, Player.SelectedMinions);
                            OnConfirm(Player.SelectedMinions);
                        }
                    }
                    break;
            }
        }

        public override void OnMouseOver(IEnumerable<Body> bodies)
        {
            
        }


        public override void OnBegin()
        {

        }

        public override void OnEnd()
        {
            //FarmPanel.TweenOut(Drawer2D.Alignment.Right, 0.25f);
            foreach (FarmTile tile in Player.Faction.FarmTiles)
            {
                Drawer3D.UnHighlightVoxel(tile.Vox);
            }
            Player.VoxSelector.Clear();
        }

        public override void Update(DwarfGame game, DwarfTime time)
        {
            if (Player.IsCameraRotationModeActive())
            {
                Player.VoxSelector.Enabled = false;
                Player.World.SetMouse(null);
                Player.BodySelector.Enabled = false;
                return;
            }

            Player.BodySelector.AllowRightClickSelection = true;

            switch (Mode)
            {
               case FarmMode.Tilling:
                    Player.VoxSelector.Enabled = true;
                    Player.VoxSelector.SelectionType = VoxelSelectionType.SelectFilled;
                    Player.BodySelector.Enabled = false;
                    ValidateTilling(Player.VoxSelector.VoxelUnderMouse);
                    break;
                case FarmMode.Planting:
                    Player.VoxSelector.Enabled = true;
                    Player.VoxSelector.SelectionType = VoxelSelectionType.SelectFilled;
                    Player.BodySelector.Enabled = false;
                    ValidatePlanting(Player.VoxSelector.VoxelUnderMouse);
                    break;
                case FarmMode.Harvesting:
                    Player.VoxSelector.Enabled = false;
                    Player.BodySelector.Enabled = true;
                    break;
                case FarmMode.WranglingAnimals:
                    Player.VoxSelector.Enabled = false;
                    Player.BodySelector.Enabled = true;
                    break;
            }

            if (Player.World.IsMouseOverGui)
                Player.World.SetMouse(Player.World.MousePointer);
            else
                Player.World.SetMouse(new Gui.MousePointer("mouse", 1, 12));
        }

        public override void Render(DwarfGame game, GraphicsDevice graphics, DwarfTime time)
        {
            NamedImageFrame frame = new NamedImageFrame("newgui/pointers", 32, 4, 1);
            switch (Mode)
            {
                case FarmMode.Tilling:
                {
                    Color drawColor = Color.PaleGoldenrod;

                    float alpha = (float) Math.Abs(Math.Sin(time.TotalGameTime.TotalSeconds*2.0f));
                    drawColor.R = (byte) (Math.Min(drawColor.R*alpha + 50, 255));
                    drawColor.G = (byte) (Math.Min(drawColor.G*alpha + 50, 255));
                    drawColor.B = (byte) (Math.Min(drawColor.B*alpha + 50, 255));

                    foreach (var tile in Player.Faction.FarmTiles)
                    {
                        if (!tile.IsTilled())
                        {
                            Drawer3D.HighlightVoxel(tile.Vox, Color.LimeGreen);
                            Drawer2D.DrawSprite(frame, tile.Vox.WorldPosition + Vector3.One * 0.5f, Vector2.One * 0.5f, Vector2.Zero, new Color(255, 255, 255, 100));
                        }
                        else
                        {
                            Drawer3D.UnHighlightVoxel(tile.Vox);
                        }
                    }
                    break;
                }
                case FarmMode.Planting:
                {
                    foreach (var tile in Player.Faction.FarmTiles)
                    {
                        if (tile.IsTilled() && !tile.PlantExists() && tile.Farmer == null)
                        {
                            Drawer3D.HighlightVoxel(tile.Vox, Color.LimeGreen);
                            Drawer2D.DrawSprite(frame, tile.Vox.WorldPosition + Vector3.One * 0.5f, Vector2.One * 0.5f, Vector2.Zero, new Color(255, 255, 255, 100));
                        }
                        else
                        {
                            Drawer3D.UnHighlightVoxel(tile.Vox);
                        }
                    }

                    break;
                }

                case FarmMode.Harvesting:
                {
                    
                    break;
                }

                case FarmMode.WranglingAnimals:
                {
                    
                    break;
                }
            }
        }

        public KillEntityTask AutoFarm()
        {
            return (from tile in Player.Faction.FarmTiles where tile.PlantExists() && tile.Plant.IsGrown && !tile.IsCanceled select new KillEntityTask(tile.Plant, KillEntityTask.KillType.Chop)).FirstOrDefault();
        }
    }
}
