# Tool Refund

Tool Refund is a Silksong mod that restores spent tools when you die.

## Installation

You can download this mod from the
[releases page](https://github.com/Esper89/Silksong-ToolRefund/releases/latest), below the
changelog. This mod requires [BepInEx](https://github.com/BepInEx/BepInEx) 5, and is installed to
your `BepInEx/plugins` directory.
[Configuration Manager](https://github.com/BepInEx/BepInEx.ConfigurationManager) can be used with
this mod.

## Usage

Once this mod is installed, by default, dying after using tools will restore all tools used since
you last rested or autosaved.

This mod can be disabled by editing its config file at `BepInEx/config/Esper89.ToolRefund.cfg` or
using Configuration Manager to set `Enabled = false` in the `[General]` section.

## Building

To build this mod for development, run `dotnet build` in the project's root directory. The output
will be in `target/Debug`. If you create a text file in the project root called `game-dirs.txt` and
input the path to your Silksong installation, the output of debug builds will be automatically
installed into that game directory.

To build this mod in release mode, run `dotnet build --configuration Release`. This will create
`target/ToolRefund.zip` for easy distribution.

## License

Copyright © 2025 Esper Thomson

This program is free software: you can redistribute it and/or modify it under the terms of version
3 of the GNU Affero General Public License as published by the Free Software Foundation.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without
even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero
General Public License for more details.

You should have received a copy of the GNU Affero General Public License along with this program.
If not, see <https://www.gnu.org/licenses>.

Additional permission under GNU AGPL version 3 section 7

If you modify this Program, or any covered work, by linking or combining it with Hollow Knight:
Silksong (or a modified version of that program), containing parts covered by the terms of its
license, the licensors of this Program grant you additional permission to convey the resulting work.
