package org.firstinspires.ftc.sim.runner;

import java.io.ByteArrayOutputStream;
import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;

/** Frame reading/writing for docs/ipc_protocol.md: int32 length + payload, big-endian, UTF-8 strings. */
final class Wire {
  private Wire() {}

  /** One received frame as a reader positioned after the length. */
  static final class In {
    private final DataInputStream data;

    In(byte[] payload) {
      data = new DataInputStream(new java.io.ByteArrayInputStream(payload));
    }

    int u8() throws IOException {
      return data.readUnsignedByte();
    }

    boolean bool() throws IOException {
      return data.readUnsignedByte() != 0;
    }

    int i32() throws IOException {
      return data.readInt();
    }

    long i64() throws IOException {
      return data.readLong();
    }

    float f32() throws IOException {
      return data.readFloat();
    }

    double f64() throws IOException {
      return data.readDouble();
    }

    String str() throws IOException {
      int n = data.readInt();
      if (n < 0) {
        return null;
      }
      byte[] bytes = new byte[n];
      data.readFully(bytes);
      return new String(bytes, StandardCharsets.UTF_8);
    }
  }

  /** A frame being built; {@link #send} prefixes the length. */
  static final class Out {
    private final ByteArrayOutputStream buffer = new ByteArrayOutputStream();
    private final DataOutputStream data = new DataOutputStream(buffer);

    Out(int type) throws IOException {
      data.writeByte(type);
    }

    Out u8(int v) throws IOException {
      data.writeByte(v);
      return this;
    }

    Out bool(boolean v) throws IOException {
      data.writeByte(v ? 1 : 0);
      return this;
    }

    Out i32(int v) throws IOException {
      data.writeInt(v);
      return this;
    }

    Out i64(long v) throws IOException {
      data.writeLong(v);
      return this;
    }

    Out f64(double v) throws IOException {
      data.writeDouble(v);
      return this;
    }

    Out str(String s) throws IOException {
      if (s == null) {
        data.writeInt(-1);
      } else {
        byte[] bytes = s.getBytes(StandardCharsets.UTF_8);
        data.writeInt(bytes.length);
        data.write(bytes);
      }
      return this;
    }

    void send(OutputStream out) throws IOException {
      byte[] payload = buffer.toByteArray();
      DataOutputStream frame = new DataOutputStream(out);
      frame.writeInt(payload.length);
      frame.write(payload);
      frame.flush();
    }
  }

  /** Reads one frame; returns null at end of stream. */
  static In read(InputStream in) throws IOException {
    DataInputStream data = new DataInputStream(in);
    int length;
    try {
      length = data.readInt();
    } catch (java.io.EOFException e) {
      return null;
    }
    byte[] payload = new byte[length];
    data.readFully(payload);
    return new In(payload);
  }
}
