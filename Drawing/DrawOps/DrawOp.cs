﻿/*
    Copyright 2015 MCGalaxy
        
    Dual-licensed under the Educational Community License, Version 2.0 and
    the GNU General Public License, Version 3 (the "Licenses"); you may
    not use this file except in compliance with the Licenses. You may
    obtain a copy of the Licenses at
    
    http://www.opensource.org/licenses/ecl2.php
    http://www.gnu.org/licenses/gpl-3.0.html
    
    Unless required by applicable law or agreed to in writing,
    software distributed under the Licenses are distributed on an "AS IS"
    BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
    or implied. See the Licenses for the specific language governing
    permissions and limitations under the Licenses.
 */
using System;
using MCGalaxy.Commands;

namespace MCGalaxy {
    
     public struct FillPos { public ushort X, Y, Z; }
     
     public struct ExtBlock { public byte Type, ExtType; }
}

namespace MCGalaxy.Drawing.Ops {
    
    public abstract class DrawOp {
        
        //public int TotalAffected; // blocks affected by the draw operation
        public int TotalModified; // blocks actually modified (e.g. some may not be due to permissions)
        
        protected internal int method;
        
        /// <summary> Minimum coordinates of the bounds of this drawing command. </summary>
        public Vector3U16 MinCoords;
        
        /// <summary> Maximum coordinates of the bounds of this drawing command. </summary>
        public Vector3U16 MaxCoords;
        
        /// <summary> Coordinates of the current block being processed by the drawing command. </summary>
        public Vector3U16 CurrentCoords;
        
        /// <summary> Whether the two given coordinates from the user should be adjusted,
        /// so that the first coordinate contains the minimum values on all three axes. </summary>
        public virtual bool MinMaxCoords { get { return true; } }
        
        /// <summary> Human friendly name of the draw operation. </summary>
        public abstract string Name { get; }
        
        /// <summary> Estimates the total number of blocks that the drawing commands affects. <br/>
        /// Note that this estimate assumes that all possibly affected blocks will be changed by the drawing command. </summary>
        public abstract int GetBlocksAffected(Level lvl, ushort x1, ushort y1, ushort z1, ushort x2, ushort y2, ushort z2);
        
        public abstract void Perform(ushort x1, ushort y1, ushort z1, ushort x2, ushort y2, ushort z2,
                                     Player p, Level lvl, Brush brush);
        
        public bool CanDraw(ushort x1, ushort y1, ushort z1, ushort x2, ushort y2, ushort z2,
                            Player p, out int affected) {
            affected = GetBlocksAffected(p.level, x1, y1, z1, x2, y2, z2);
            if (affected > p.group.maxBlocks) {
                Player.SendMessage(p, "You tried to draw " + affected + " blocks.");
                Player.SendMessage(p, "You cannot draw more than " + p.group.maxBlocks + ".");
                return false;
            }
            return true;
        }
        
        public virtual bool DetermineDrawOpMethod(Level lvl, int affected) {
            if (affected > Server.DrawReloadLimit) {
                method = MethodSetTile;
                return true;
            } else {
                method = lvl.bufferblocks ? MethodBlockQueue : MethodBlockChange;
                return false;
            }
        }
        
        public virtual void Reset() {
            //TotalAffected = 0;
            TotalModified = 0;
        }
        
        protected void PlaceBlock(Player p, Level lvl, ushort x, ushort y, ushort z, Brush brush) {
            CurrentCoords.X = x; CurrentCoords.Y = y; CurrentCoords.Z = z;
            byte type = brush.NextBlock(this);
            if (type == Block.Zero) return;
            PlaceBlock(p, lvl, x, y, z, type, brush.NextExtBlock(this));
        }
        
        protected void PlaceBlock(Player p, Level lvl, ushort x, ushort y, ushort z, byte type, byte extType) {
            switch (method) {
                case MethodBlockQueue:
                    BlockQueue.Addblock(p, x, y, z, type, extType);
                    TotalModified++;
                    break;
                case MethodBlockChange:
                    p.level.Blockchange(p, x, y, z, type, extType);
                    TotalModified++;
                    break;
                case MethodSetTile:
                    byte old = lvl.GetTile(x, y, z);
                    if (old == Block.Zero || !lvl.CheckAffectPermissions(p, x, y, z, old, type))
                        return;
                    p.level.SetTile(x, y, z, type, p, extType);
                    p.loginBlocks++;
                    p.overallBlocks++;
                    TotalModified++;
                    break;
            }
        }
        
        internal const int MethodBlockQueue = 0, MethodBlockChange = 1, MethodSetTile = 2;
        
        public static bool DoDrawOp(DrawOp op, Brush brush, Player p,
                                           ushort x1, ushort y1, ushort z1, ushort x2, ushort y2, ushort z2) {
            int affected = 0;
            op.MinCoords = Vector3U16.Min(x1, y1, z1, x2, y2, z2);
            op.MaxCoords = Vector3U16.Max(x1, y1, z1, x2, y2, z2);
            if (op.MinMaxCoords) {
                ushort xx1 = x1, yy1 = y1, zz1 = z1, xx2 = x2, yy2 = y2, zz2 = z2;
                x1 = Math.Min(xx1, xx2); x2 = Math.Max(xx1, xx2);
                y1 = Math.Min(yy1, yy2); y2 = Math.Max(yy1, yy2);
                z1 = Math.Min(zz1, zz2); z2 = Math.Max(zz1, zz2);
            }
            
            if (!op.CanDraw(x1, y1, z1, x2, y2, z2, p, out affected))
                return false;
            Player.SendMessage(p, op.Name + ": affecting up to an estimated " + affected + " blocks");
            
            bool needReveal = op.DetermineDrawOpMethod(p.level, affected);
            op.Perform(x1, y1, z1, x2, y2, z2, p, p.level, brush);
            if (needReveal) {
                foreach (Player pl in PlayerInfo.players) {
                    if (pl.level.name.ToLower() == p.level.name.ToLower())
                        CmdReveal.ReloadMap(p, pl, true);
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            return true;
        }
    }
}
