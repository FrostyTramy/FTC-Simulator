package com.qualcomm.robotcore.hardware;

/** Any device in the hardware map (SDK 12.0 public API). */
public interface HardwareDevice {
  enum Manufacturer {
    Unknown,
    Other,
    Lego,
    HiTechnic,
    ModernRobotics,
    Adafruit,
    Matrix,
    Lynx,
    AMS,
    STMicroelectronics,
    Broadcom,
    DFRobot,
    DigitalChickenLabs,
    SparkFun,
    MaxBotix,
    LimelightVision,
    GoBilda
  }

  Manufacturer getManufacturer();

  String getDeviceName();

  String getConnectionInfo();

  int getVersion();

  void resetDeviceConfigurationForOpMode();

  void close();
}
