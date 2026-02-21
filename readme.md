# WaySharpTest
Simple test application for [WaySharp](../WaySharp/)

This is a simple appllication to demonstrate how to use [WaySharp](../WaySharp/).
Since it is very early in development, few features are implemented, and there will
probably be marshalling errors and memory leaks, but it is a fully functional
proof-of-concept to demonstrate creating an application window (`xdg_toplevel`),
hooking into global Wayland interfaces such as the Display (`wl_display`),
Compositor (`wl_compositor`), SharedMemory (`wl_shm`), as well as the XDG windowing
protocol (`xdg_wm_base`).

To build and run, you'll need to clone both WaySharp and WaySharpTest into a common
parent directory (i.e., WaySharp and WaySharp will both be separate subdirectories
within a parent directory), you'll need the .NET 10 SDK, and you should just be able
to run with:
```
dotnet run
```

The application window draws a simple checkerboard pattern by writing directly to
the surface's graphical buffer. It also responds to size changes, and will exit
gracefully on a Close request.
