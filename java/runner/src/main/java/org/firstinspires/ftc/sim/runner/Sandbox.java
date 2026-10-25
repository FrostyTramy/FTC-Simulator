package org.firstinspires.ftc.sim.runner;

import java.io.File;
import java.io.FilePermission;
import java.net.URL;
import java.nio.file.Path;
import java.security.CodeSource;
import java.security.Permission;
import java.security.Permissions;
import java.security.Policy;
import java.security.ProtectionDomain;
import java.util.PropertyPermission;

/**
 * Sandbox for team code (spec 01 "Security", docs/ipc_protocol.md "Sandbox"). The runner is a separate process; on
 * top of that a Java security policy (JDK 17 SecurityManager) gives classes from the team's jar only these
 * permissions: read files, write and delete inside the session work directory, read system properties, reflect on
 * declared members. Whenever team code is on the call stack everything else is denied: starting processes, network
 * connections, System.exit, native libraries, new class loaders, environment variables, private access to other
 * code, changing the security manager or the policy. Runner, shim and JDK code keep all permissions.
 */
@SuppressWarnings("removal")
final class Sandbox extends Policy {
  private final String teamCodeLocation;
  private final Permissions teamCode = new Permissions();

  Sandbox(URL teamCodeJar, Path workDir) {
    teamCodeLocation = teamCodeJar.toExternalForm();
    String dir = workDir.toAbsolutePath().normalize().toString();
    teamCode.add(new FilePermission("<<ALL FILES>>", "read"));
    teamCode.add(new FilePermission(dir, "read,write,delete"));
    teamCode.add(new FilePermission(dir + File.separator + "-", "read,write,delete"));
    teamCode.add(new PropertyPermission("*", "read"));
    teamCode.add(new RuntimePermission("accessDeclaredMembers"));
    teamCode.setReadOnly();
  }

  /** Installs the policy and the security manager (call after opening the simulator socket). */
  static void install(URL teamCodeJar, Path workDir) {
    Policy.setPolicy(new Sandbox(teamCodeJar, workDir));
    System.setSecurityManager(new SecurityManager());
  }

  @Override
  public boolean implies(ProtectionDomain domain, Permission permission) {
    CodeSource source = domain.getCodeSource();
    URL location = source != null ? source.getLocation() : null;
    if (location != null && teamCodeLocation.equals(location.toExternalForm())) {
      return teamCode.implies(permission);
    }
    return true;
  }
}
