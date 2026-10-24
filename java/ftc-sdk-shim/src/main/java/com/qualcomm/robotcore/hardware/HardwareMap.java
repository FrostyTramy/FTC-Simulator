package com.qualcomm.robotcore.hardware;

import android.content.Context;
import com.qualcomm.robotcore.eventloop.opmode.OpModeManagerNotifier;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.SortedSet;
import java.util.TreeSet;

/**
 * The configured devices by name (SDK 12.0 public API, subset). Lookups behave like the SDK: names are
 * case sensitive, a device of the wrong type is "not found", and the error messages match the SDK.
 */
public class HardwareMap implements Iterable<HardwareDevice> {
  /** Devices of one type by name, e.g. {@code hardwareMap.dcMotor.get("left")}. */
  public static class DeviceMapping<DEVICE_TYPE extends HardwareDevice> implements Iterable<DEVICE_TYPE> {
    private final Map<String, DEVICE_TYPE> map = new LinkedHashMap<>();
    private final Class<DEVICE_TYPE> deviceTypeClass;

    public DeviceMapping(Class<DEVICE_TYPE> deviceTypeClass) {
      this.deviceTypeClass = deviceTypeClass;
    }

    public Class<DEVICE_TYPE> getDeviceTypeClass() {
      return deviceTypeClass;
    }

    public DEVICE_TYPE cast(Object obj) {
      return deviceTypeClass.cast(obj);
    }

    public DEVICE_TYPE get(String deviceName) {
      DEVICE_TYPE device = map.get(deviceName.trim());
      if (device == null) {
        throw new IllegalArgumentException(String.format("Unable to find a hardware device with the name \"%s\"", deviceName));
      }
      return device;
    }

    public void put(String deviceName, DEVICE_TYPE device) {
      map.put(deviceName.trim(), device);
    }

    public void putLocal(String deviceName, DEVICE_TYPE device) {
      put(deviceName, device);
    }

    public boolean contains(String deviceName) {
      return map.containsKey(deviceName.trim());
    }

    public boolean remove(String deviceName) {
      return map.remove(deviceName.trim()) != null;
    }

    @Override
    public Iterator<DEVICE_TYPE> iterator() {
      return new ArrayList<>(map.values()).iterator();
    }

    public Set<Map.Entry<String, DEVICE_TYPE>> entrySet() {
      return Collections.unmodifiableSet(map.entrySet());
    }

    public int size() {
      return map.size();
    }
  }

  public DeviceMapping<DcMotorController> dcMotorController = new DeviceMapping<>(DcMotorController.class);
  public DeviceMapping<DcMotor> dcMotor = new DeviceMapping<>(DcMotor.class);
  public DeviceMapping<ServoController> servoController = new DeviceMapping<>(ServoController.class);
  public DeviceMapping<Servo> servo = new DeviceMapping<>(Servo.class);
  public DeviceMapping<CRServo> crservo = new DeviceMapping<>(CRServo.class);
  public DeviceMapping<VoltageSensor> voltageSensor = new DeviceMapping<>(VoltageSensor.class);

  public final List<DeviceMapping<? extends HardwareDevice>> allDeviceMappings =
      List.of(dcMotorController, dcMotor, servoController, servo, crservo, voltageSensor);
  public final Context appContext;

  private final Map<String, List<HardwareDevice>> allDevicesMap = new LinkedHashMap<>();
  private final List<HardwareDevice> allDevicesList = new ArrayList<>();

  public HardwareMap(Context appContext, OpModeManagerNotifier notifier) {
    this.appContext = appContext;
  }

  /** The device of the given type configured under {@code deviceName}. */
  public <T> T get(Class<? extends T> classOrInterface, String deviceName) {
    T result = tryGet(classOrInterface, deviceName);
    if (result == null) {
      throw new IllegalArgumentException(String.format("Unable to find a hardware device with name \"%s\" and type %s",
          deviceName, classOrInterface.getSimpleName()));
    }
    return result;
  }

  /** Like {@link #get(Class, String)} but returns null when there is no such device. */
  public <T> T tryGet(Class<? extends T> classOrInterface, String deviceName) {
    List<HardwareDevice> devices = allDevicesMap.get(deviceName.trim());
    if (devices == null) {
      return null;
    }
    for (HardwareDevice device : devices) {
      if (classOrInterface.isInstance(device)) {
        return classOrInterface.cast(device);
      }
    }
    return null;
  }

  /** Any device configured under {@code deviceName}. */
  public HardwareDevice get(String deviceName) {
    List<HardwareDevice> devices = allDevicesMap.get(deviceName.trim());
    if (devices == null || devices.isEmpty()) {
      throw new IllegalArgumentException(String.format("Unable to find a hardware device with name \"%s\"", deviceName));
    }
    return devices.get(0);
  }

  public <T> List<T> getAll(Class<? extends T> classOrInterface) {
    List<T> result = new ArrayList<>();
    for (HardwareDevice device : allDevicesList) {
      if (classOrInterface.isInstance(device)) {
        result.add(classOrInterface.cast(device));
      }
    }
    return result;
  }

  public SortedSet<String> getAllNames(Class<? extends HardwareDevice> classOrInterface) {
    SortedSet<String> names = new TreeSet<>();
    for (Map.Entry<String, List<HardwareDevice>> e : allDevicesMap.entrySet()) {
      for (HardwareDevice d : e.getValue()) {
        if (classOrInterface.isInstance(d)) {
          names.add(e.getKey());
        }
      }
    }
    return names;
  }

  @SuppressWarnings("unchecked")
  public void put(String deviceName, HardwareDevice device) {
    String name = deviceName.trim();
    allDevicesMap.computeIfAbsent(name, k -> new ArrayList<>()).add(device);
    if (!allDevicesList.contains(device)) {
      allDevicesList.add(device);
    }
    for (DeviceMapping<? extends HardwareDevice> mapping : allDeviceMappings) {
      if (mapping.getDeviceTypeClass().isInstance(device)) {
        ((DeviceMapping<HardwareDevice>) mapping).put(name, device);
      }
    }
  }

  public boolean remove(String deviceName, HardwareDevice device) {
    List<HardwareDevice> list = allDevicesMap.get(deviceName.trim());
    boolean removed = list != null && list.remove(device);
    if (removed) {
      allDevicesList.remove(device);
      for (DeviceMapping<? extends HardwareDevice> mapping : allDeviceMappings) {
        mapping.remove(deviceName);
      }
    }
    return removed;
  }

  public Set<String> getNamesOf(HardwareDevice device) {
    Set<String> names = new LinkedHashSet<>();
    for (Map.Entry<String, List<HardwareDevice>> e : allDevicesMap.entrySet()) {
      if (e.getValue().contains(device)) {
        names.add(e.getKey());
      }
    }
    return names;
  }

  public int size() {
    return allDevicesList.size();
  }

  @Override
  public Iterator<HardwareDevice> iterator() {
    return new ArrayList<>(allDevicesList).iterator();
  }

  public Iterable<HardwareDevice> unsafeIterable() {
    return allDevicesList;
  }

  public void logDevices() {}
}
