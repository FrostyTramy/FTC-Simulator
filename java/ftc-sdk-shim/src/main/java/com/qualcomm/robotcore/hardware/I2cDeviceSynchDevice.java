package com.qualcomm.robotcore.hardware;

/** Base of the SDK's I2C sensor drivers (SDK 12.0 public API, subset). */
public abstract class I2cDeviceSynchDevice<DEVICE_CLIENT extends I2cDeviceSynchSimple> implements HardwareDevice {
  protected DEVICE_CLIENT deviceClient;
  protected boolean deviceClientIsOwned;
  protected boolean isInitialized;

  protected I2cDeviceSynchDevice(DEVICE_CLIENT deviceClient, boolean deviceClientIsOwned) {
    this.deviceClient = deviceClient;
    this.deviceClientIsOwned = deviceClientIsOwned;
  }

  protected void registerArmingStateCallback(boolean doInitialize) {
    if (doInitialize) {
      initializeIfNecessary();
    }
  }

  protected void engage() {}

  protected void disengage() {}

  public DEVICE_CLIENT getDeviceClient() {
    return deviceClient;
  }

  protected synchronized void initializeIfNecessary() {
    if (!isInitialized) {
      initialize();
    }
  }

  public synchronized boolean initialize() {
    isInitialized = doInitialize();
    return isInitialized;
  }

  protected abstract boolean doInitialize();

  @Override
  public void resetDeviceConfigurationForOpMode() {}

  @Override
  public void close() {}

  @Override
  public int getVersion() {
    return 1;
  }

  @Override
  public String getConnectionInfo() {
    return deviceClient.getConnectionInfo();
  }
}
