﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace RPLIDAR_Mapping {
    
    
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "17.13.0.0")]
    internal sealed partial class AppSettings : global::System.Configuration.ApplicationSettingsBase {
        
        private static AppSettings defaultInstance = ((AppSettings)(global::System.Configuration.ApplicationSettingsBase.Synchronized(new AppSettings())));
        
        public static AppSettings Default {
            get {
                return defaultInstance;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("3000")]
        public int WindowWidth {
            get {
                return ((int)(this["WindowWidth"]));
            }
            set {
                this["WindowWidth"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("2000")]
        public int WindowHeight {
            get {
                return ((int)(this["WindowHeight"]));
            }
            set {
                this["WindowHeight"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("nyUu4MgAWhTG")]
        public string WiFiPassword {
            get {
                return ((string)(this["WiFiPassword"]));
            }
            set {
                this["WiFiPassword"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("Telia-2G-B9A841")]
        public string WiFiSSID {
            get {
                return ((string)(this["WiFiSSID"]));
            }
            set {
                this["WiFiSSID"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool AllowResizing {
            get {
                return ((bool)(this["AllowResizing"]));
            }
            set {
                this["AllowResizing"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("192.168.0.147")]
        public string mqttServer {
            get {
                return ((string)(this["mqttServer"]));
            }
            set {
                this["mqttServer"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("1883")]
        public int mqttPort {
            get {
                return ((int)(this["mqttPort"]));
            }
            set {
                this["mqttPort"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("COM3")]
        public string SerialPort {
            get {
                return ((string)(this["SerialPort"]));
            }
            set {
                this["SerialPort"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("1")]
        public float MapZoom {
            get {
                return ((float)(this["MapZoom"]));
            }
            set {
                this["MapZoom"] = value;
            }
        }
        
        [global::System.Configuration.ApplicationScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("2")]
        public int GridTileSizeCM {
            get {
                return ((int)(this["GridTileSizeCM"]));
            }
        }
        
        [global::System.Configuration.ApplicationScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("1")]
        public float GridSizeM {
            get {
                return ((float)(this["GridSizeM"]));
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("10000")]
        public int MaxPoints {
            get {
                return ((int)(this["MaxPoints"]));
            }
            set {
                this["MaxPoints"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("0")]
        public int MinPointQuality {
            get {
                return ((int)(this["MinPointQuality"]));
            }
            set {
                this["MinPointQuality"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("3")]
        public int MainMapNGrids {
            get {
                return ((int)(this["MainMapNGrids"]));
            }
            set {
                this["MainMapNGrids"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("10")]
        public int GridScaleCMtoPixels {
            get {
                return ((int)(this["GridScaleCMtoPixels"]));
            }
            set {
                this["GridScaleCMtoPixels"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("1000")]
        public int GridPixels {
            get {
                return ((int)(this["GridPixels"]));
            }
            set {
                this["GridPixels"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool DrawGrids {
            get {
                return ((bool)(this["DrawGrids"]));
            }
            set {
                this["DrawGrids"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("serial")]
        public string CommunicationProtocol {
            get {
                return ((string)(this["CommunicationProtocol"]));
            }
            set {
                this["CommunicationProtocol"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("50")]
        public int LIDARDataBatchSize {
            get {
                return ((int)(this["LIDARDataBatchSize"]));
            }
            set {
                this["LIDARDataBatchSize"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("10")]
        public int TileDecayRate {
            get {
                return ((int)(this["TileDecayRate"]));
            }
            set {
                this["TileDecayRate"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("60")]
        public int ICPUpdateRate {
            get {
                return ((int)(this["ICPUpdateRate"]));
            }
            set {
                this["ICPUpdateRate"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("1.5")]
        public float MaxHighintensityFactor {
            get {
                return ((float)(this["MaxHighintensityFactor"]));
            }
            set {
                this["MaxHighintensityFactor"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("50")]
        public int MapUpdateBatchSize {
            get {
                return ((int)(this["MapUpdateBatchSize"]));
            }
            set {
                this["MapUpdateBatchSize"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("20")]
        public float TileTrustIncrement {
            get {
                return ((float)(this["TileTrustIncrement"]));
            }
            set {
                this["TileTrustIncrement"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("2")]
        public float TileTrustDecrement {
            get {
                return ((float)(this["TileTrustDecrement"]));
            }
            set {
                this["TileTrustDecrement"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("30")]
        public int TileTrustThreshold {
            get {
                return ((int)(this["TileTrustThreshold"]));
            }
            set {
                this["TileTrustThreshold"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("3000")]
        public int MapWindowWidth {
            get {
                return ((int)(this["MapWindowWidth"]));
            }
            set {
                this["MapWindowWidth"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("3000")]
        public int MapWindowHeight {
            get {
                return ((int)(this["MapWindowHeight"]));
            }
            set {
                this["MapWindowHeight"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("10")]
        public float TilemergeThreshold {
            get {
                return ((float)(this["TilemergeThreshold"]));
            }
            set {
                this["TilemergeThreshold"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("10")]
        public int MergeTilesFrequency {
            get {
                return ((int)(this["MergeTilesFrequency"]));
            }
            set {
                this["MergeTilesFrequency"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool DrawMergedTiles {
            get {
                return ((bool)(this["DrawMergedTiles"]));
            }
            set {
                this["DrawMergedTiles"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("10")]
        public int MinMergedTileSize {
            get {
                return ((int)(this["MinMergedTileSize"]));
            }
            set {
                this["MinMergedTileSize"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("1")]
        public float GridScaleFactor {
            get {
                return ((float)(this["GridScaleFactor"]));
            }
            set {
                this["GridScaleFactor"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("5")]
        public int DrawTileSize {
            get {
                return ((int)(this["DrawTileSize"]));
            }
            set {
                this["DrawTileSize"] = value;
            }
        }
    }
}
