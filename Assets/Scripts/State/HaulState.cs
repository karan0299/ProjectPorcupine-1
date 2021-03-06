﻿#region License
// ====================================================
// Project Porcupine Copyright(C) 2016 Team Porcupine
// This program comes with ABSOLUTELY NO WARRANTY; This is free software,
// and you are welcome to redistribute it under certain conditions; See
// file LICENSE, which is part of this source code package, for details.
// ====================================================
#endregion
using System.Collections.Generic;
using System.Linq;
using ProjectPorcupine.Pathfinding;
using UnityEngine;

namespace ProjectPorcupine.Entities.States
{
    public enum HaulAction
    {
        DumpMaterial,
        FindMaterial,
        PickupMaterial,
        DeliverMaterial,
        DropOffmaterial
    }

    public class HaulState : State
    {
        private bool noMoreMaterialFound = false;

        public HaulState(Character character, Job job, State nextState = null)
            : base("Haul", character, nextState)
        {
            Job = job;
        }

        private Job Job { get; set; }

        public override void Update(float deltaTime)
        {
            List<Tile> path = null;
            HaulAction nextAction = NextAction();

            DebugLog(" - next action: {0}", nextAction);

            switch (nextAction)
            {
                case HaulAction.DumpMaterial:
                    character.SetState(new DumpState(character, this));
                    break;

                case HaulAction.FindMaterial:
                    // Find material somewhere
                    string[] inventoryTypes = character.Inventory != null ?
                        new string[] { character.Inventory.Type } :
                        Job.RequestedItems.Keys.ToArray();
                    path = World.Current.InventoryManager.GetPathToClosestInventoryOfType(inventoryTypes, character.CurrTile, Job.canTakeFromStockpile);
                    if (path != null && path.Count > 0)
                    {
                        Inventory inv = path.Last().Inventory;
                        inv.Claim(character, (inv.AvailableInventory < Job.RequestedItems[inv.Type].AmountDesired()) ? inv.AvailableInventory : Job.RequestedItems[inv.Type].AmountDesired());
                        character.SetState(new MoveState(character, Pathfinder.GoalTileEvaluator(path.Last(), false), path, this));
                    }
                    else if (character.Inventory == null)
                    {
                        // The character has no inventory and can't find anything to haul.
                        Interrupt();
                        DebugLog(" - Nothing to haul");
                        Finished();
                    }
                    else
                    {
                        noMoreMaterialFound = true;
                    }

                    break;

                case HaulAction.PickupMaterial:
                    Inventory tileInventory = character.CurrTile.Inventory;
                    int amountCarried = character.Inventory != null ? character.Inventory.StackSize : 0;
                    int amount = Mathf.Min(Job.AmountDesiredOfInventoryType(tileInventory.Type) - amountCarried, tileInventory.StackSize);
                    DebugLog(" - Picked up {0} {1}", amount, tileInventory.Type);
                    World.Current.InventoryManager.PlaceInventory(character, tileInventory, amount);
                    break;

                case HaulAction.DeliverMaterial:
                    path = Pathfinder.FindPath(character.CurrTile, Job.IsTileAtJobSite, Pathfinder.DefaultDistanceHeuristic(Job.tile));
                    if (path != null && path.Count > 0)
                    {
                        character.SetState(new MoveState(character, Pathfinder.GoalTileEvaluator(path.Last(), false), path, this));
                    }
                    else
                    {
                        Job.AddCharCantReach(character);
                        character.InterruptState();
                    }

                    break;

                case HaulAction.DropOffmaterial:
                    DebugLog(" - Delivering {0} {1}", character.Inventory.StackSize, character.Inventory.Type);
                    World.Current.InventoryManager.PlaceInventory(Job, character);

                    // Ping the Job system
                    Job.DoWork(0);
                    Finished();
                    break;
            }
        }

        private HaulAction NextAction()
        {
            Inventory tileInventory = character.CurrTile.Inventory;
            bool jobWantsTileInventory = InventoryManager.CanBePickedUp(tileInventory, Job.canTakeFromStockpile) &&
                                         Job.AmountDesiredOfInventoryType(tileInventory.Type) > 0;

            if (noMoreMaterialFound && character.Inventory != null)
            {
                return Job.IsTileAtJobSite(character.CurrTile) ? HaulAction.DropOffmaterial : HaulAction.DeliverMaterial;
            }
            else if (character.Inventory != null && Job.AmountDesiredOfInventoryType(character.Inventory.Type) == 0)
            {
                return HaulAction.DumpMaterial;
            }
            else if (character.Inventory == null)
            {
                return jobWantsTileInventory ? HaulAction.PickupMaterial : HaulAction.FindMaterial;
            }
            else
            {
                int amountWanted = Job.AmountDesiredOfInventoryType(character.Inventory.Type);
                int currentlyCarrying = character.Inventory.StackSize;
                int spaceAvailable = character.Inventory.MaxStackSize - currentlyCarrying;

                // Already carrying it
                if (amountWanted <= currentlyCarrying || spaceAvailable == 0)
                {
                    return Job.IsTileAtJobSite(character.CurrTile) ? HaulAction.DropOffmaterial : HaulAction.DeliverMaterial;
                }
                else
                {
                    // Can carry more and want more
                    return jobWantsTileInventory ? HaulAction.PickupMaterial : HaulAction.FindMaterial;
                }
            }
        }
    }
}
