/*
	Copyright 2010 MCSharp team (Modified for use with MCZall/MCLawl/MCGalaxy)
	
	Dual-licensed under the	Educational Community License, Version 2.0 and
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
namespace MCGalaxy.Commands {
	
    public sealed class CmdBind : Command {
        public override string name { get { return "bind"; } }
        public override string shortcut { get { return ""; } }
        public override string type { get { return CommandTypes.Building; } }
        public override bool museumUsable { get { return false; } }
        public override LevelPermission defaultRank { get { return LevelPermission.AdvBuilder; } }

        public override void Use(Player p, string message) {
            if (message == "") { Help(p); return; }
            if (p == null) { MessageInGameOnly(p); return; }
            string[] args = message.ToLower().Split(' ');
            if (args.Length > 2) { Help(p); return; }
            
            if (args[0] == "clear") {
                for (byte id = 0; id < 128; id++) p.bindings[id] = id;
                Player.SendMessage(p, "All bindings were unbound.");
                return;
            }

            if (args.Length == 2) {
            	byte b1 = Block.Byte(args[0]);
            	byte b2 = Block.Byte(args[1]);
            	if (b1 == Block.Zero) { Player.SendMessage(p, "There is no block \"" + args[0] + "\"."); return; }
            	if (b2 == Block.Zero) { Player.SendMessage(p, "There is no block \"" + args[1] + "\"."); return; }

                if (!Block.Placable(b1)) { Player.SendMessage(p, Block.Name(b1) + " isn't a special block."); return; }
                if (!Block.canPlace(p, b2)) { Player.SendMessage(p, "You cannot bind " + Block.Name(b2) + "."); return; }
                if (b1 >= Block.CpeCount) { Player.SendMessage(p, "Cannot bind anything to this block."); return; }
                
                p.bindings[b1] = b2;
                Player.SendMessage(p, Block.Name(b1) + " bound to " + Block.Name(b2) + ".");
            } else {
            	byte b = Block.Byte(args[0]);
                if (b >= Block.CpeCount) { Player.SendMessage(p, "This block cannot be bound"); return; }

                if (p.bindings[b] == b) { Player.SendMessage(p, Block.Name(b) + " isn't bound."); return; }
                p.bindings[b] = b; Player.SendMessage(p, "Unbound " + Block.Name(b) + ".");
            }
        }
        
        public override void Help(Player p) {
            Player.SendMessage(p, "/bind <block> [type] - Replaces block with type.");
            Player.SendMessage(p, "/bind clear - Clears all binds.");
        }
    }
}
