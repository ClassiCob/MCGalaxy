/*
	Copyright 2015 MCGalaxy team
	
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
namespace MCGalaxy.Commands
{
    public sealed class CmdMark : Command
    {
        public override string name { get { return "mark"; } }
        public override string shortcut { get { return "m"; } }
        public override string type { get { return CommandTypes.Building; } }
        public override bool museumUsable { get { return false; } }
        public override LevelPermission defaultRank { get { return LevelPermission.Guest; } }
        public CmdMark() { }

        public override void Use(Player p, string message)
        {
            if (p == null) { MessageInGameOnly(p); return; }
            
            int x = (ushort)(p.pos[0] / 32);
            int y = (ushort)((p.pos[1] / 32) - 1);
            int z = (ushort)(p.pos[2] / 32);
            Command.all.Find("click").Use(p, x + " " + y + " " + z);
        }
        public override void Help(Player p)
        {
            Player.SendMessage(p, "/mark - Clicks where you are standing.");
            Player.SendMessage(p, "Use this to place a marker at your position when making a selection or cuboid.");
        }
    }
}
