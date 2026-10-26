package org.firstinspires.ftc.sim.devices;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/**
 * The goBILDA Pinpoint's registers (as in com.qualcomm.hardware.gobilda.GoBildaPinpointDriver, GBDRV) for a team's own
 * copy of the goBILDA driver (D-039). Values are little-endian 32-bit ints or floats; BULK_READ returns status, loop
 * time, both encoders (ints) and x, y, heading and their velocities (floats), 40 bytes.
 */
public final class PinpointRegisters implements RegisterMap {
  private static final int DEVICE_CONTROL = 4;
  private static final int BULK_READ = 18;
  private static final int CALIBRATING = 1 << 1;

  /** Register number → (snapshot index, float?) ; index -1 = write only. */
  private static int index(int register) {
    return switch (register) {
      case 1 -> 10;   // DEVICE_ID
      case 2 -> 11;   // DEVICE_VERSION
      case 3 -> 0;    // DEVICE_STATUS
      case 5 -> 1;    // LOOP_TIME
      case 6 -> 2;    // X_ENCODER_VALUE
      case 7 -> 3;    // Y_ENCODER_VALUE
      case 8 -> 4;    // X_POSITION
      case 9 -> 5;    // Y_POSITION
      case 10 -> 6;   // H_ORIENTATION
      case 11 -> 7;   // X_VELOCITY
      case 12 -> 8;   // Y_VELOCITY
      case 13 -> 9;   // H_VELOCITY
      case 14 -> 13;  // MM_PER_TICK
      case 15 -> 14;  // X_POD_OFFSET
      case 16 -> 15;  // Y_POD_OFFSET
      case 17 -> 12;  // YAW_SCALAR
      default -> -1;
    };
  }

  private static boolean isFloat(int register) {
    return register >= 8 && register <= 17;
  }

  private static final int[] BULK = {3, 5, 6, 7, 8, 9, 10, 11, 12, 13};

  @Override
  public byte[] read(SimI2cDevice device, int register, int count) {
    double[] v = device.read();
    ByteBuffer out = ByteBuffer.allocate(Math.max(count, 40)).order(ByteOrder.LITTLE_ENDIAN);
    for (int r : register == BULK_READ ? BULK : new int[] {register}) {
      int i = index(r);
      double value = i >= 0 && i < v.length ? v[i] : 0;
      if (isFloat(r)) {
        out.putFloat((float) value);
      } else {
        out.putInt((int) value);
      }
    }
    byte[] bytes = new byte[count];
    System.arraycopy(out.array(), 0, bytes, 0, count);
    return bytes;
  }

  @Override
  public void write(SimI2cDevice device, int register, byte[] data) {
    byte[] padded = new byte[4];
    System.arraycopy(data, 0, padded, 0, Math.min(4, data.length));
    ByteBuffer in = ByteBuffer.wrap(padded).order(ByteOrder.LITTLE_ENDIAN);
    if (isFloat(register)) {
      float value = in.getFloat();
      device.write("write", new double[] {register, value}, index(register), value);
      return;
    }
    int value = in.getInt();
    if (register == DEVICE_CONTROL && (value & (1 << 1)) != 0) {        // reset position and IMU
      device.write("write", new double[] {register, value}, 0, CALIBRATING, 4, 0, 5, 0, 6, 0);
    } else if (register == DEVICE_CONTROL && (value & 1) != 0) {         // recalibrate IMU
      device.write("write", new double[] {register, value}, 0, CALIBRATING);
    } else {
      device.write("write", new double[] {register, value});
    }
  }
}
