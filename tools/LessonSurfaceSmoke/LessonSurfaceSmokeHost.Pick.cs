using System.Diagnostics;
using System.Runtime.InteropServices;
using HeyGanesh.LessonSurface;
using HeyGanesh.PartLibrary;
using HeyGanesh.PartLibrary.Models;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonSurfaceSmoke;

/// <summary>
/// LSU-15: click a part to ask about it (DEC-031) on the real scene window, with the real mouse. The
/// clicks go where the page says each part is (<see cref="SceneSnapshot.PickPoints"/>), so a row passes
/// only if a real click there comes back as exactly that part. Moves the cursor; it is put back after.
/// Needs an interactive desktop with nothing covering the scene window.
/// </summary>
internal static partial class LessonSurfaceSmokeHost
{
    private static async Task<int> RunAssertPickAsync()
    {
        var failures = 0;
        PickNative.GetCursorPos(out var homeCursor);
        try
        {
            await using var surface = await CardSurface.CreateAsync();
            await new LessonPlayer(surface, new PauseSpeaker(60)).PlayAsync(Validated3d(SceneLesson(FourStrokeEngine.Id)));
            var engine = ModelLibrary.Find(FourStrokeEngine.Id)!;
            var start = await surface.DescribeSceneAsync();
            var rodKey = "m.connecting-rod";
            var hidden = engine.Parts.Select(p => p.Id).Where(id => !start.PickPoints.ContainsKey("m." + id)).ToList();
            failures = Check(start.PickPoints.ContainsKey(rodKey) && start.PickPoints.Count >= engine.Parts.Count / 2,
                $"engine drawn: {start.PickPoints.Count} of {engine.Parts.Count} parts have a visible point to click, the connecting rod among them (none found for: {string.Join(", ", hidden)})", failures);
            if (!start.PickPoints.TryGetValue(rodKey, out var rod))
            {
                return Summarize(failures + 1, "PICK OK");
            }

            // Picking off: the page must not even post the click.
            Click(rod);
            await Task.Delay(400);
            var off = await surface.DescribeSceneAsync();
            failures = Check(!off.Picking && surface.RefusedPicks == 0, "a click before picking is on posts nothing (picking off, 0 refused)", failures);

            // Picking on: a real click on the rod comes back as the rod, without moving or activating the window.
            var clock = Stopwatch.StartNew();
            var picked = await ClickWhilePickingAsync(surface, rod, TimeSpan.FromSeconds(10));
            failures = Check(picked == new PartPick("m", FourStrokeEngine.Id, "connecting-rod"), $"picking on: a real click on the rod -> PartPick(m, {FourStrokeEngine.Id}, connecting-rod) in {clock.ElapsedMilliseconds} ms ({picked})", failures);
            var after = await surface.DescribeSceneAsync();
            failures = Check(!after.Picking && after.Bounds == start.Bounds && !surface.EverTookFocus,
                "after the pick: picking off again, the window did not move (no drag), never foreground", failures);

            // Every part the camera can see picks itself, and only itself.
            var wrong = new List<string>();
            foreach (var (key, point) in start.PickPoints)
            {
                var pick = await ClickWhilePickingAsync(surface, point, TimeSpan.FromSeconds(5));
                if (pick is null || $"{pick.InstanceId}.{pick.PartId}" != key)
                {
                    wrong.Add($"{key}->{(pick is null ? "none" : pick.PartId)}");
                }
            }

            failures = Check(wrong.Count == 0, $"all {start.PickPoints.Count} visible parts pick themselves ({(wrong.Count == 0 ? "none wrong" : string.Join(", ", wrong))})", failures);

            // Empty background: nothing picked; the wait ends at its idle timeout.
            var corner = new Point(start.Bounds.X + 14, start.Bounds.Y + start.Bounds.Height - 14);
            var background = await ClickWhilePickingAsync(surface, corner, TimeSpan.FromMilliseconds(1500));
            failures = Check(background is null && surface.RefusedPicks == 0, "a click on empty background picks nothing; the wait ends at its idle timeout", failures);

            // Forged page messages are refused, and the wait they tried to answer is still open.
            var waiting = surface.PickAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            await UntilPickingAsync(surface);
            var refused = await surface.ProbePickGuardAsync();
            var stillWaiting = !waiting.IsCompleted;
            Click(rod);
            var real = await waiting;
            failures = Check(refused == 3 && stillWaiting && real?.PartId == "connecting-rod",
                $"3 forged clicks (unknown instance, unknown part, non-string ids) refused ({refused}); the wait stayed open and the next real click was taken", failures);

            // The close button ends a wait at once and hides the lesson; after that nothing is offered.
            clock.Restart();
            var closed = await ClickWhilePickingAsync(surface, start.CloseButton, TimeSpan.FromSeconds(10));
            var gone = await surface.DescribeSceneAsync();
            failures = Check(closed is null && clock.ElapsedMilliseconds < 3000 && !gone.Visible,
                $"the close button ends the wait at once ({clock.ElapsedMilliseconds} ms) and hides the scene", failures);
            var none = await surface.PickAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            failures = Check(none is null, "with the scene closed, a pick returns null without waiting", failures);
        }
        finally
        {
            PickNative.SetCursorPos(homeCursor.X, homeCursor.Y);
        }

        return Summarize(failures, "PICK OK");
    }

    /// <summary>Starts a pick, waits until the page has picking on, clicks <paramref name="at"/>, and returns the pick.</summary>
    private static async Task<PartPick?> ClickWhilePickingAsync(CardSurface surface, Point at, TimeSpan idle)
    {
        var pick = surface.PickAsync(idle, CancellationToken.None);
        await UntilPickingAsync(surface);
        Click(at);
        return await pick;
    }

    private static async Task UntilPickingAsync(CardSurface surface)
    {
        for (var i = 0; i < 40 && !(await surface.DescribeSceneAsync()).Picking; i++)
        {
            await Task.Delay(50);
        }
    }

    /// <summary>
    /// A real left click at a screen point (global physical pixels). Move, down and up go to Windows in
    /// ONE SendInput call, so they reach the input queue together and a hand on the physical mouse
    /// cannot slip a move between them. If the cursor still was not on the point afterwards (someone
    /// moved the mouse, or Windows refused the input), that is reported, so a failure here reads as
    /// interference rather than as a wrong part.
    /// </summary>
    private static void Click(Point at)
    {
        var desk = SystemInformation.VirtualScreen;
        int Norm(int v, int origin, int size) => (int)Math.Round((v - origin) * 65535.0 / Math.Max(1, size - 1));
        var inputs = new[]
        {
            new PickNative.Input
            {
                Type = PickNative.InputMouse,
                Mouse = new PickNative.MouseInput
                {
                    Dx = Norm(at.X, desk.X, desk.Width),
                    Dy = Norm(at.Y, desk.Y, desk.Height),
                    Flags = PickNative.Move | PickNative.Absolute | PickNative.VirtualDesk,
                },
            },
            new PickNative.Input { Type = PickNative.InputMouse, Mouse = new PickNative.MouseInput { Flags = PickNative.LeftDown } },
            new PickNative.Input { Type = PickNative.InputMouse, Mouse = new PickNative.MouseInput { Flags = PickNative.LeftUp } },
        };
        var sent = PickNative.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<PickNative.Input>());
        Thread.Sleep(80);
        PickNative.GetCursorPos(out var now);
        if (sent != inputs.Length || Math.Abs(now.X - at.X) > 2 || Math.Abs(now.Y - at.Y) > 2)
        {
            Console.WriteLine($"          note: click at ({at.X},{at.Y}) -- {sent}/{inputs.Length} inputs accepted, cursor now ({now.X},{now.Y}); mouse in use or input blocked?");
        }
    }

    /// <summary>Test-host input only (scan-forbidden.ps1 exempts tools/): the product never synthesizes input.</summary>
    private static class PickNative
    {
        public const uint InputMouse = 0;
        public const uint LeftDown = 0x0002;
        public const uint LeftUp = 0x0004;
        public const uint Move = 0x0001;
        public const uint Absolute = 0x8000;
        public const uint VirtualDesk = 0x4000;

        [StructLayout(LayoutKind.Sequential)]
        public struct MouseInput
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        // INPUT is a union of mouse/keyboard/hardware input; only the mouse arm is used, sized to the largest.
        [StructLayout(LayoutKind.Explicit)]
        public struct Input
        {
            [FieldOffset(0)]
            public uint Type;

            [FieldOffset(8)]
            public MouseInput Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint count, Input[] inputs, int size);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out NativePoint point);
    }
}
