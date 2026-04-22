# CStancer

Advanced Stance Tuning for FiveM.

## Features
- **Wheel Stance**: Modify track width and camber per-axle (Front, Rear, All, Center).
- **Suspension**: Fine-tune suspension height.
- **Wheel Geometry**: Adjust wheel size, wheel width, and tire collider size.
- **Optimized Sync**: Uses a cached 1-second sync loop with State Bags for high performance and reliable network synchronization.
- **Persistence**: Stance settings remain applied when entities enter/exit network scope.

## Installation
1. Move the `dist/cstancer` folder to your resources.
2. Add `ensure cstancer` to your server config.

## Commands
- `/cstancer`: Open the stance tuning menu.

## Building
Run `dotnetbat.bat` to recompile the C# project. Requires .NET 4.5.2 and FiveM dependencies.
