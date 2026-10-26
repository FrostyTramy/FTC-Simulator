package com.qualcomm.robotcore.util;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;

/** Byte array conversions used by I2C drivers (SDK 12.0 public API; big-endian unless an order is given). */
public class TypeConversion {
  public static byte[] shortToByteArray(short value) {
    return shortToByteArray(value, ByteOrder.BIG_ENDIAN);
  }

  public static byte[] shortToByteArray(short value, ByteOrder order) {
    return ByteBuffer.allocate(2).order(order).putShort(value).array();
  }

  public static byte[] intToByteArray(int value) {
    return intToByteArray(value, ByteOrder.BIG_ENDIAN);
  }

  public static byte[] intToByteArray(int value, ByteOrder order) {
    return ByteBuffer.allocate(4).order(order).putInt(value).array();
  }

  public static byte[] longToByteArray(long value) {
    return longToByteArray(value, ByteOrder.BIG_ENDIAN);
  }

  public static byte[] longToByteArray(long value, ByteOrder order) {
    return ByteBuffer.allocate(8).order(order).putLong(value).array();
  }

  public static short byteArrayToShort(byte[] bytes) {
    return byteArrayToShort(bytes, ByteOrder.BIG_ENDIAN);
  }

  public static short byteArrayToShort(byte[] bytes, ByteOrder order) {
    return byteArrayToShort(bytes, 0, order);
  }

  public static short byteArrayToShort(byte[] bytes, int ibFirst, ByteOrder order) {
    return ByteBuffer.wrap(bytes, ibFirst, 2).order(order).getShort();
  }

  public static int byteArrayToInt(byte[] bytes) {
    return byteArrayToInt(bytes, ByteOrder.BIG_ENDIAN);
  }

  public static int byteArrayToInt(byte[] bytes, ByteOrder order) {
    return ByteBuffer.wrap(bytes).order(order).getInt();
  }

  public static long byteArrayToLong(byte[] bytes) {
    return byteArrayToLong(bytes, ByteOrder.BIG_ENDIAN);
  }

  public static long byteArrayToLong(byte[] bytes, ByteOrder order) {
    return ByteBuffer.wrap(bytes).order(order).getLong();
  }

  public static int unsignedByteToInt(byte b) {
    return b & 0xFF;
  }

  public static double unsignedByteToDouble(byte b) {
    return b & 0xFF;
  }

  public static int unsignedShortToInt(short s) {
    return s & 0xFFFF;
  }

  public static long unsignedIntToLong(int i) {
    return i & 0xFFFFFFFFL;
  }

  public static byte[] stringToUtf8(String javaString) {
    return javaString.getBytes(StandardCharsets.UTF_8);
  }

  public static String utf8ToString(byte[] utf8) {
    return new String(utf8, StandardCharsets.UTF_8);
  }
}
