package org.firstinspires.ftc.robotcore.external.hardware.camera;

import com.qualcomm.robotcore.hardware.HardwareDevice;
import com.qualcomm.robotcore.util.SerialNumber;

/** A USB webcam in the hardware map (SDK 12.0 public API). */
public interface WebcamName extends CameraName, HardwareDevice {
  SerialNumber getSerialNumber();

  String getUsbDeviceNameIfAttached();

  boolean isAttached();
}
