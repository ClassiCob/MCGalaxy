/*
    Copyright 2010 MCSharp team (Modified for use with MCZall/MCLawl/MCGalaxy)

    Dual-licensed under the    Educational Community License, Version 2.0 and
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
using MCGalaxy.Drawing;
using MCGalaxy.Drawing.Ops;

namespace MCGalaxy.Commands
{
    public sealed class CmdPyramid : DrawCmd
    {
        public override string name { get { return "pyramid"; } }
        public override string shortcut { get { return "pd"; } }

        protected override void Blockchange2(Player p, ushort x, ushort y, ushort z, byte type, byte extType) {
            RevertAndClearState(p, x, y, z);
            CatchPos cpos = (CatchPos)p.blockchangeObject;
            GetRealBlock(type, extType, p, ref cpos);
            DrawOp drawOp = null;
            Brush brush = new SolidBrush(cpos.type, cpos.extType);
            
            if (y != cpos.y) {
                Player.SendMessage(p, "The two edges of the pyramid must be on the same level");
                return;
            }

            switch (cpos.solid) {
                case SolidType.solid:
                    drawOp = new PyramidSolidDrawOp(); break;
                case SolidType.hollow:
                    drawOp = new PyramidHollowDrawOp(); break;
                case SolidType.reverse:
                    drawOp = new PyramidReverseDrawOp(); break;
            }
            
            if (!DrawOp.DoDrawOp(drawOp, brush, p, cpos.x, cpos.y, cpos.z, x, y, z))
                return;
            if (p.staticCommands)
                p.Blockchange += new Player.BlockchangeEventHandler(Blockchange1);
        }
        
        protected override SolidType GetType(string msg) {
            if (msg == "solid")
                return SolidType.solid;
            else if (msg == "hollow")
                return SolidType.hollow;
            else if (msg == "reverse")
                return SolidType.reverse;
            return SolidType.Invalid;
        }
        
        public override void Help(Player p) {
            Player.SendMessage(p, "/pyramid [type] <solid/hollow/reverse> - create a square pyramid of blocks.");
        }
    }
}
