using System;
using System.Threading;
using DbzLegendsRemaster.SLPS_003_55;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PsxSdkMonogame;

namespace DbzLegendsRemaster;

public class Game1 : Game
{
    private const int FrameWidth = 320;
    private const int FrameHeight = 240;

    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;
    private Texture2D _frameTexture;
    private byte[] _framePixels;
    private Color[] _frameColors;
    private SLPS_003_55_exe _slps_003_55_exe;
    private Thread _gameThread;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
        IsMouseVisible = true;
        _graphics.PreferredBackBufferWidth = 1280;
        _graphics.PreferredBackBufferHeight = 960;
        Window.Title = "Dragon Ball Z: The Legend";
        Exiting += OnExiting;
    }

    protected override void Initialize()
    {
        PsxSdkBridges.Install();
        _slps_003_55_exe = new SLPS_003_55_exe();
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _frameTexture = new Texture2D(GraphicsDevice, FrameWidth, FrameHeight);
        _framePixels = new byte[FrameWidth * FrameHeight * 3];
        _frameColors = new Color[FrameWidth * FrameHeight];

        // JUSTIFICATION: backend MonoGame only — the original main loop blocks inside VSync and
        // must remain intact; FrameBaton transfers one completed logical frame to this host thread.
        _gameThread = new Thread(RunGameThread)
        {
            IsBackground = true,
            Name = "DbzLegendsRuntime",
        };
        _gameThread.Start();
    }

    // JUSTIFICATION: backend MonoGame only — owns the translated runtime thread and reports its
    // completion or fault to the host without changing original game control flow.
    private void RunGameThread()
    {
        try
        {
            _slps_003_55_exe.Main();
            FrameBaton.CompleteRuntime();
        }
        catch (GameShutdownException)
        {
        }
        catch (Exception exception)
        {
            FrameBaton.CaptureFault(exception);
        }
    }

    protected override void Update(GameTime gameTime)
    {
        if (FrameBaton.PendingFault is { } fault)
        {
            throw new InvalidOperationException("DbzLegendsRuntime thread faulted", fault);
        }

        if (Keyboard.GetState().IsKeyDown(Keys.Escape))
        {
            Exit();
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);

        bool runtimeActive = !FrameBaton.RuntimeCompleted;
        if (runtimeActive)
        {
            FrameBaton.WaitFrameReadyAndPresent();
        }

        PresentFrame();

        if (runtimeActive && !FrameBaton.RuntimeCompleted)
        {
            FrameBaton.ReleaseGame();
        }

        base.Draw(gameTime);
    }

    // JUSTIFICATION: backend MonoGame only — uploads the PSX display window and presents it with
    // integer nearest-neighbour scaling while the runtime thread is parked at the frame baton.
    private void PresentFrame()
    {
        LibGpu.ReadDisplayRgb24(_framePixels, FrameWidth, FrameHeight);
        for (int index = 0, offset = 0; index < _frameColors.Length; index++, offset += 3)
        {
            _frameColors[index] = new Color(
                _framePixels[offset],
                _framePixels[offset + 1],
                _framePixels[offset + 2]);
        }

        _frameTexture.SetData(_frameColors);
        float scale = Math.Min(
            (float)GraphicsDevice.PresentationParameters.BackBufferWidth / FrameWidth,
            (float)GraphicsDevice.PresentationParameters.BackBufferHeight / FrameHeight);
        scale = Math.Max(1f, MathF.Floor(scale));
        var origin = new Vector2(
            (GraphicsDevice.PresentationParameters.BackBufferWidth - FrameWidth * scale) / 2f,
            (GraphicsDevice.PresentationParameters.BackBufferHeight - FrameHeight * scale) / 2f);

        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        _spriteBatch.Draw(_frameTexture, origin, null, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        _spriteBatch.End();
    }

    // JUSTIFICATION: backend MonoGame only — unblocks and joins the runtime thread before the
    // graphics/audio host is disposed.
    private void OnExiting(object sender, EventArgs args)
    {
        FrameBaton.RequestShutdown();
        _gameThread?.Join(TimeSpan.FromSeconds(2));
        LibSpu.AudioBackend.Dispose();
    }
}
