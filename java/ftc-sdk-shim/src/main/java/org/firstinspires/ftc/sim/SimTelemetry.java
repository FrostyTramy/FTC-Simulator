package org.firstinspires.ftc.sim;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import org.firstinspires.ftc.robotcore.external.Func;
import org.firstinspires.ftc.robotcore.external.Telemetry;

/**
 * Telemetry like the SDK's TelemetryImpl (spec 06): items and lines, "caption : value" with " | "
 * between items of a line, update() transmits at most every msTransmissionInterval (default 250 ms,
 * virtual time) and with auto-clear removes the non-retained items, plus a log of 9 lines.
 */
public final class SimTelemetry implements Telemetry {
  private final SimRuntime runtime;
  private final List<Object> entries = new ArrayList<>(); // ItemImpl or LineImpl, in display order
  private final LogImpl log = new LogImpl();
  private final Map<Object, Runnable> actions = new LinkedHashMap<>();
  private boolean autoClear = true;
  private int msTransmissionInterval = 250;
  private String itemSeparator = " | ";
  private String captionValueSeparator = " : ";
  private DisplayFormat displayFormat = DisplayFormat.CLASSIC;
  private long lastTransmitNanos = Long.MIN_VALUE / 2;
  private List<String> lastTransmitted = List.of();
  private long transmissions;

  public SimTelemetry(SimRuntime runtime) {
    this.runtime = runtime;
  }

  /** Lines shown on the DS after the latest transmission. */
  public synchronized List<String> lastTransmitted() {
    return lastTransmitted;
  }

  /** Number of transmissions so far (the runner sends telemetry when this changes). */
  public synchronized long transmissions() {
    return transmissions;
  }

  public synchronized DisplayFormat displayFormat() {
    return displayFormat;
  }

  // ------------------------------------------------------------------ items and lines

  private final class ItemImpl implements Item {
    private String caption;
    private Object value;
    private Boolean retained;
    private final LineImpl parent;

    ItemImpl(LineImpl parent, String caption, Object value) {
      this.parent = parent;
      this.caption = caption;
      this.value = value;
    }

    String render() {
      Object v = value;
      if (v instanceof Supplier) {
        v = ((Supplier) v).get();
      }
      return caption + captionValueSeparator + v;
    }

    @Override
    public String getCaption() {
      return caption;
    }

    @Override
    public Item setCaption(String caption) {
      this.caption = caption;
      return this;
    }

    @Override
    public Item setValue(String format, Object... args) {
      value = String.format(Locale.US, format, args);
      return this;
    }

    @Override
    public Item setValue(Object value) {
      this.value = String.valueOf(value);
      return this;
    }

    @Override
    public <T> Item setValue(Func<T> valueProducer) {
      value = (Supplier) () -> String.valueOf(valueProducer.value());
      return this;
    }

    @Override
    public <T> Item setValue(String format, Func<T> valueProducer) {
      value = (Supplier) () -> String.format(Locale.US, format, valueProducer.value());
      return this;
    }

    @Override
    public Item setRetained(Boolean retained) {
      this.retained = retained;
      return this;
    }

    @Override
    public boolean isRetained() {
      return retained != null ? retained : value instanceof Supplier;
    }

    @Override
    public Item addData(String caption, String format, Object... args) {
      return line().addData(caption, format, args);
    }

    @Override
    public Item addData(String caption, Object value) {
      return line().addData(caption, value);
    }

    @Override
    public <T> Item addData(String caption, Func<T> valueProducer) {
      return line().addData(caption, valueProducer);
    }

    @Override
    public <T> Item addData(String caption, String format, Func<T> valueProducer) {
      return line().addData(caption, format, valueProducer);
    }

    private LineImpl line() {
      if (parent != null) {
        return parent;
      }
      // Items added to an item go on the same line: turn this item into a line.
      LineImpl line = new LineImpl("");
      int index = entries.indexOf(this);
      line.items.add(new ItemImpl(line, caption, value));
      entries.set(index, line);
      return line;
    }
  }

  private interface Supplier {
    String get();
  }

  private final class LineImpl implements Line {
    private final String caption;
    private final List<ItemImpl> items = new ArrayList<>();

    LineImpl(String caption) {
      this.caption = caption;
    }

    String render() {
      StringBuilder sb = new StringBuilder(caption);
      for (int i = 0; i < items.size(); i++) {
        if (i > 0 || caption.length() > 0) {
          sb.append(i > 0 ? itemSeparator : "");
        }
        sb.append(items.get(i).render());
      }
      return sb.toString();
    }

    private Item add(ItemImpl item) {
      synchronized (SimTelemetry.this) {
        items.add(item);
      }
      return item;
    }

    @Override
    public Item addData(String caption, String format, Object... args) {
      return add(new ItemImpl(this, caption, String.format(Locale.US, format, args)));
    }

    @Override
    public Item addData(String caption, Object value) {
      return add(new ItemImpl(this, caption, String.valueOf(value)));
    }

    @Override
    public <T> Item addData(String caption, Func<T> valueProducer) {
      return add(new ItemImpl(this, caption, (Supplier) () -> String.valueOf(valueProducer.value())));
    }

    @Override
    public <T> Item addData(String caption, String format, Func<T> valueProducer) {
      return add(new ItemImpl(this, caption, (Supplier) () -> String.format(Locale.US, format, valueProducer.value())));
    }
  }

  @Override
  public synchronized Item addData(String caption, String format, Object... args) {
    ItemImpl item = new ItemImpl(null, caption, String.format(Locale.US, format, args));
    entries.add(item);
    return item;
  }

  @Override
  public synchronized Item addData(String caption, Object value) {
    ItemImpl item = new ItemImpl(null, caption, String.valueOf(value));
    entries.add(item);
    return item;
  }

  @Override
  public synchronized <T> Item addData(String caption, Func<T> valueProducer) {
    ItemImpl item = new ItemImpl(null, caption, (Supplier) () -> String.valueOf(valueProducer.value()));
    entries.add(item);
    return item;
  }

  @Override
  public synchronized <T> Item addData(String caption, String format, Func<T> valueProducer) {
    ItemImpl item = new ItemImpl(null, caption, (Supplier) () -> String.format(Locale.US, format, valueProducer.value()));
    entries.add(item);
    return item;
  }

  @Override
  public synchronized boolean removeItem(Item item) {
    return entries.remove(item);
  }

  @Override
  public synchronized void clear() {
    entries.removeIf(e -> !(e instanceof ItemImpl i && i.isRetained()));
  }

  @Override
  public synchronized void clearAll() {
    entries.clear();
    log.clear();
  }

  @Override
  public synchronized Object addAction(Runnable action) {
    Object token = new Object();
    actions.put(token, action);
    return token;
  }

  @Override
  public synchronized boolean removeAction(Object token) {
    return actions.remove(token) != null;
  }

  @Override
  public void speak(String text) {}

  @Override
  public void speak(String text, String languageCode, String countryCode) {}

  /** Transmits if the transmission interval has passed; with auto-clear the items are cleared. */
  @Override
  public synchronized boolean update() {
    runtime.consume(runtime.costs().trivialCallNanos);
    long now = runtime.nowNanos();
    boolean transmit = now - lastTransmitNanos >= msTransmissionInterval * 1_000_000L;
    if (transmit) {
      for (Runnable action : actions.values()) {
        action.run();
      }
      List<String> lines = new ArrayList<>();
      for (Object e : entries) {
        lines.add(e instanceof ItemImpl i ? i.render() : ((LineImpl) e).render());
      }
      lines.addAll(log.lines());
      lastTransmitted = List.copyOf(lines);
      lastTransmitNanos = now;
      transmissions++;
    }
    if (autoClear) {
      clear();
    }
    return transmit;
  }

  @Override
  public synchronized Line addLine() {
    return addLine("");
  }

  @Override
  public synchronized Line addLine(String lineCaption) {
    LineImpl line = new LineImpl(lineCaption);
    entries.add(line);
    return line;
  }

  @Override
  public synchronized boolean removeLine(Line line) {
    return entries.remove(line);
  }

  @Override
  public synchronized boolean isAutoClear() {
    return autoClear;
  }

  @Override
  public synchronized void setAutoClear(boolean autoClear) {
    this.autoClear = autoClear;
  }

  @Override
  public synchronized int getMsTransmissionInterval() {
    return msTransmissionInterval;
  }

  @Override
  public synchronized void setMsTransmissionInterval(int msTransmissionInterval) {
    this.msTransmissionInterval = msTransmissionInterval;
  }

  @Override
  public synchronized String getItemSeparator() {
    return itemSeparator;
  }

  @Override
  public synchronized void setItemSeparator(String itemSeparator) {
    this.itemSeparator = itemSeparator;
  }

  @Override
  public synchronized String getCaptionValueSeparator() {
    return captionValueSeparator;
  }

  @Override
  public synchronized void setCaptionValueSeparator(String captionValueSeparator) {
    this.captionValueSeparator = captionValueSeparator;
  }

  @Override
  public synchronized void setDisplayFormat(DisplayFormat displayFormat) {
    this.displayFormat = displayFormat;
  }

  @Override
  public Log log() {
    return log;
  }

  private final class LogImpl implements Log {
    private final List<String> lines = new ArrayList<>();
    private int capacity = 9;
    private DisplayOrder order = DisplayOrder.OLDEST_FIRST;

    List<String> lines() {
      List<String> out = new ArrayList<>(lines);
      if (order == DisplayOrder.NEWEST_FIRST) {
        java.util.Collections.reverse(out);
      }
      return out;
    }

    @Override
    public int getCapacity() {
      return capacity;
    }

    @Override
    public void setCapacity(int capacity) {
      this.capacity = capacity;
      trim();
    }

    @Override
    public DisplayOrder getDisplayOrder() {
      return order;
    }

    @Override
    public void setDisplayOrder(DisplayOrder displayOrder) {
      order = displayOrder;
    }

    @Override
    public void add(String entry) {
      synchronized (SimTelemetry.this) {
        lines.add(entry);
        trim();
      }
    }

    @Override
    public void add(String format, Object... args) {
      add(String.format(Locale.US, format, args));
    }

    @Override
    public void clear() {
      synchronized (SimTelemetry.this) {
        lines.clear();
      }
    }

    private void trim() {
      while (lines.size() > capacity) {
        lines.remove(0);
      }
    }
  }
}
