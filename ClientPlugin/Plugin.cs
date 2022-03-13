using System;
using System.IO;
using ClientPlugin.GUI;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using Shared.Config;
using Shared.Logging;
using Shared.Patches;
using Shared.Plugin;
using VRage.FileSystem;
using VRage.Plugins;
// Non Base Plugin Libs
using Linearstar.Windows.RawInput;
using System.Windows.Forms;
using VRageMath;
using Sandbox.Game.World;
using System.Linq;

namespace MoreInput
{
    // ReSharper disable once UnusedType.Global
    public class Plugin : IPlugin, ICommonPlugin
    {
        public const string Name = "MoreInput";
        public static Plugin Instance { get; private set; }

        public long Tick { get; private set; }

        public IPluginLogger Log => Logger;
        private static readonly IPluginLogger Logger = new PluginLogger(Name);

        public IPluginConfig Config => config?.Data;
        private PersistentConfig<PluginConfig> config;
        private static readonly string ConfigFileName = $"{Name}.cfg";

        private static bool initialized;
        private static bool failed;

        // My Shit
        RawInputReceiverWindow rirwindow;
        Vector3 MovInput;
        Vector3 RotInput;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Init(object gameInstance)
        {
            Instance = this;

            Log.Info("Loading");

            var configPath = Path.Combine(MyFileSystem.UserDataPath, ConfigFileName);
            config = PersistentConfig<PluginConfig>.Load(Log, configPath);

            Common.SetPlugin(this);

            if (!PatchHelpers.HarmonyPatchAll(Log, new Harmony(Name)))
            {
                failed = true;
                return;
            }

            Log.Debug("Successfully loaded");
        }

        public void Dispose()
        {
            try
            {
                RawInputDevice.UnregisterDevice(new HidUsageAndPage(0x01, 0x08));
                // TODO: Save state and close resources here, called when the game exists (not guaranteed!)
                // IMPORTANT: Do NOT call harmony.UnpatchAll() here! It may break other plugins.
            }
            catch (Exception ex)
            {
                Log.Critical(ex, "Dispose failed");
            }

            Instance = null;
        }

        public void Update()
        {
            EnsureInitialized();
            try
            {
                if (!failed)
                {
                    CustomUpdate();
                    Tick++;
                }
            }
            catch (Exception ex)
            {
                Log.Critical(ex, "Update failed");
                failed = true;
            }
        }

        private void EnsureInitialized()
        {
            if (initialized || failed)
                return;

            Log.Info("Initializing");
            try
            {
                Initialize();
            }
            catch (Exception ex)
            {
                Log.Critical(ex, "Failed to initialize plugin");
                failed = true;
                return;
            }

            Log.Debug("Successfully initialized");
            initialized = true;
        }

        private void Initialize()
        {
            rirwindow = new RawInputReceiverWindow();

            rirwindow.Input += (sender, e) =>
            {
                byte[] data = e.Data.ToStructure();
                int inputType = data.Skip(32).First();
                byte[] inputData = data.Skip(33).Take(6).ToArray();
                
                if (inputType == 1)
                {
                    MovInput = new Vector3(
                        -(((sbyte)inputData[0 + 1] << 8) + inputData[0]),
                        -(((sbyte)inputData[0 + 1] << 8) + inputData[1]),
                        -(((sbyte)inputData[2 + 1] << 8) + inputData[2])
                        );
                    MovInput /= 350; //Max 350
                }

                if (inputType == 2)
                {
                    RotInput = new Vector3(
                        -(((sbyte)inputData[0 + 1] << 8) + inputData[0]),
                        -(((sbyte)inputData[1 + 1] << 8) + inputData[1]),
                        -(((sbyte)inputData[2 + 1] << 8) + inputData[2])
                        );
                    RotInput /= 350;
                }

                //if (inputType == 3) { keys = inputData[0]; }
            };

            RawInputDevice.RegisterDevice(new HidUsageAndPage(0x01, 0x08), RawInputDeviceFlags.ExInputSink, rirwindow.Handle);
            //Application.Run();
        }

        private void CustomUpdate()
        {
            if (MovInput == Vector3.Zero && RotInput == Vector3.Zero)
            {
                MySession.Static.ControlledEntity?.MoveAndRotateStopped();
            }
            else
            {
                //MovInput = Vector3.Clamp(MovInput, -Vector3.One, Vector3.One);
                Vector2 rotatePY = new Vector2(RotInput.X, RotInput.Z);
                float rotateR = RotInput.Y;
                MySession.Static.ControlledEntity?.MoveAndRotate(MovInput, rotatePY, rotateR);
            }

        }


        // ReSharper disable once UnusedMember.Global
        public void OpenConfigDialog()
        {
            MyGuiSandbox.AddScreen(new MyPluginConfigDialog());
        }
    }

    class RawInputEventArgs : EventArgs
    {
        public RawInputEventArgs(RawInputData data)
        {
            Data = data;
        }

        public RawInputData Data { get; }
    }

    class RawInputReceiverWindow : NativeWindow
    {
        public event EventHandler<RawInputEventArgs> Input;

        public RawInputReceiverWindow()
        {
            CreateHandle(new CreateParams
            {
                X = 0,
                Y = 0,
                Width = 0,
                Height = 0,
                Style = 0x800000,
            });
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_INPUT = 0x00FF;

            if (m.Msg == WM_INPUT)
            {
                var data = RawInputData.FromHandle(m.LParam);

                Input?.Invoke(this, new RawInputEventArgs(data));
            }

            base.WndProc(ref m);
        }
    }
}