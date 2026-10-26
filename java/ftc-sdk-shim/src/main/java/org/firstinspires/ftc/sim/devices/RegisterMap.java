package org.firstinspires.ftc.sim.devices;

/**
 * Raw register access to a simulated I2C device, for a team's own copy of a vendor driver (D-039): register reads are
 * answered from the device's simulated values and writes become the same commands the simulator's driver sends.
 */
public interface RegisterMap {
  /** One read transaction of {@code count} bytes starting at {@code register}. */
  byte[] read(SimI2cDevice device, int register, int count);

  /** One write transaction to {@code register}. */
  void write(SimI2cDevice device, int register, byte[] data);
}
