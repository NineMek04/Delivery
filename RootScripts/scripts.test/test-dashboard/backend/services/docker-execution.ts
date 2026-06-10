import Docker from 'dockerode';
import fs from 'fs';
import path from 'path';
import { ArtifactService } from './artifact-service';

function resolvePolicyPath(): string {
  const candidates = [
    process.env.SANDBOX_POLICY_PATH,
    path.join(process.cwd(), 'config', 'sandbox-policy.json'),
    path.join(__dirname, '..', 'config', 'sandbox-policy.json'),
    path.join(__dirname, '..', '..', 'config', 'sandbox-policy.json'),
  ].filter(Boolean) as string[];

  const policyPath = candidates.find((candidate) => fs.existsSync(candidate));
  if (!policyPath) {
    throw new Error(`Unable to locate sandbox-policy.json. Checked: ${candidates.join(', ')}`);
  }
  return policyPath;
}

const POLICY_PATH = resolvePolicyPath();
const policy = JSON.parse(fs.readFileSync(POLICY_PATH, 'utf-8'));

// Initialize Dockerode against the local Docker Engine.
const dockerOptions: any = {};
if (process.env.DOCKER_HOST_PROXY) {
  dockerOptions.host = process.env.DOCKER_HOST_PROXY;
  dockerOptions.port = 2375;
}
const docker = new Docker(dockerOptions);

// Store active container references for cancellation
const activeContainers = new Map<string, Docker.Container>();

export async function runTestInDocker(
  sessionId: string,
  suiteType: string,
  onLog: (chunk: string) => void
): Promise<string | null> {
  const suiteConfig = policy.testSuites[suiteType];
  if (!suiteConfig) {
    throw new Error(`Unknown test suite type: ${suiteType}`);
  }

  const { dockerImage, dockerCommand, timeoutSeconds } = suiteConfig;
  const sandbox = policy.sandbox;

  // Workspace configuration
  const hostWorkspacePath = process.env.HOST_WORKSPACE_PATH || path.resolve(process.cwd(), '..', '..', '..');
  
  onLog(`\r\n=== Starting ${suiteConfig.name} in Sandbox ===\r\n`);
  onLog(`Image: ${dockerImage}\r\n`);
  onLog(`Command: ${dockerCommand}\r\n`);
  onLog(`Workspace Bind: ${hostWorkspacePath} -> /workspace\r\n\r\n`);

  try {
    // 1. Pull Image if not available locally
    onLog(`[Docker] Verifying image ${dockerImage}...\r\n`);
    const images = await docker.listImages({ filters: { reference: [dockerImage] } });
    if (images.length === 0) {
      onLog(`[Docker] Pulling image ${dockerImage}... This may take a moment.\r\n`);
      await new Promise<void>((resolve, reject) => {
        docker.pull(dockerImage, {}, (err: any, stream: any) => {
          if (err) return reject(err);
          docker.modem.followProgress(stream, onFinished, onProgress);

          function onFinished(err: any, output: any) {
            if (err) return reject(err);
            resolve();
          }
          function onProgress(event: any) {
            if (event.status) {
              onLog(`[Docker] Pull progress: ${event.status} ${event.progress || ''}\r\n`);
            }
          }
        });
      });
      onLog(`[Docker] Image pulled successfully.\r\n`);
    } else {
      onLog(`[Docker] Image found locally.\r\n`);
    }

    // 2. Prepare Sandbox Security & Mount configurations
    const hostConfig: Docker.HostConfig = {
      Binds: [
        `${hostWorkspacePath}:/workspace`,
        '//var/run/docker.sock:/var/run/docker.sock'
      ],
      Memory: sandbox.memoryLimitBytes,
      CpuShares: sandbox.cpuShares,
      NetworkMode: sandbox.networkMode,
      SecurityOpt: sandbox.securityOpts,
      CapDrop: sandbox.capabilitiesDrop,
      ReadonlyRootfs: sandbox.readOnlyRootFilesystem,
      AutoRemove: false,
      ExtraHosts: ["host.docker.internal:host-gateway"]
    };

    onLog(`[Docker] Creating container...\r\n`);

    // 3. Create Container
    // We split dockerCommand into command array or pass via sh -c
    const containerCmd = ['sh', '-c', dockerCommand];

    const container = await docker.createContainer({
      Image: dockerImage,
      Cmd: containerCmd,
      WorkingDir: '/workspace',
      HostConfig: hostConfig,
      Env: [
        "TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal",
        "TESTCONTAINERS_RYUK_DISABLED=true"
      ],
      Labels: {
        'delivery.test.session': sessionId,
        'delivery.test.suite': suiteType,
      },
      Tty: true, // Allocates a pseudo-TTY for a real-time console look
      AttachStdout: true,
      AttachStderr: true
    });

    // Save container reference for cancellation
    activeContainers.set(sessionId, container);

    onLog(`[Docker] Starting container (ID: ${container.id.substring(0, 12)})...\r\n`);

    // 4. Start Container
    await container.start();
    const waitResult = container.wait();

    // 5. Attach logs and stream
    const logStream = await container.logs({
      follow: true,
      stdout: true,
      stderr: true
    });

    // 6. Setup execution timeout
    let wasTimeout = false;
    const timeoutHandle = setTimeout(async () => {
      wasTimeout = true;
      onLog(`\r\n[Timeout] Container execution timed out after ${timeoutSeconds}s!\r\n`);
      await cancelDockerTest(sessionId);
    }, timeoutSeconds * 1000);

    // 7. Stream logs chunk by chunk
    await new Promise<void>((resolve, reject) => {
      logStream.on('data', (chunk: Buffer) => {
        const text = chunk.toString();
        onLog(text);
      });

      logStream.on('end', () => {
        clearTimeout(timeoutHandle);
        resolve();
      });

      logStream.on('error', (err) => {
        clearTimeout(timeoutHandle);
        reject(err);
      });
    });

    const result = await waitResult;

    // 8. Clean up tracking reference
    activeContainers.delete(sessionId);

    if (wasTimeout) {
      throw new Error(`Test execution timed out after ${timeoutSeconds} seconds.`);
    }

    // 9. Extract /tmp/results.* from container before removal
    let reportData: string | null = null;
    try {
      const getArchive = async (filePath: string) => {
        try {
          const tarStream = await container.getArchive({ path: filePath });
          return await new Promise<string>((resolve, reject) => {
            const extract = require('tar-stream').extract();
            let data = '';
            extract.on('entry', function(header: any, stream: any, next: any) {
              stream.on('data', function(chunk: any) {
                data += chunk.toString('utf8');
              });
              stream.on('end', function() {
                next();
              });
              stream.resume();
            });
            extract.on('finish', function() {
              resolve(data);
            });
            extract.on('error', reject);
            tarStream.pipe(extract);
          });
        } catch (err) {
          return null; // File not found
        }
      };

      const jsonReport = await getArchive('/tmp/results.json');
      if (jsonReport) {
        reportData = jsonReport;
      } else {
        const trxReport = await getArchive('/tmp/results.trx');
        if (trxReport) {
          reportData = trxReport;
        }
      }
    } catch (e: any) {
      console.error(`[Docker] Failed to extract report from container: ${e.message}`);
    }

    // 10. Check Container ExitCode before cleanup
    try {
      const exitCode = result.StatusCode;
      onLog(`\r\n=== Container finished with Exit Code: ${exitCode} ===\r\n`);
      if (exitCode !== 0) {
        throw new Error(`Test container exited with non-zero code: ${exitCode}`);
      }
    } finally {
      await container.remove({ force: true }).catch(() => {});
    }
    
    return reportData;

  } catch (error: any) {
    activeContainers.delete(sessionId);
    onLog(`\r\n[Error] Docker Execution Failed: ${error.message}\r\n`);
    throw error;
  }
}

export async function cancelDockerTest(sessionId: string): Promise<boolean> {
  let container = activeContainers.get(sessionId);

  if (!container) {
    try {
      const matches = await docker.listContainers({
        all: true,
        filters: {
          label: [`delivery.test.session=${sessionId}`]
        }
      });
      if (matches.length > 0) {
        container = docker.getContainer(matches[0].Id);
      } else {
        // Javascript robust fallback
        const allContainers = await docker.listContainers({ all: true });
        const matched = allContainers.find(c => c.Labels && c.Labels['delivery.test.session'] === sessionId);
        if (matched) {
          container = docker.getContainer(matched.Id);
        }
      }
    } catch (err) {
      console.error('[Docker] List containers error:', err);
    }
  }

  if (!container) return false;

  try {
    console.log(`[Docker] Cancelling test session ${sessionId}, sending SIGINT (Ctrl+C) to container ${container.id.substring(0, 12)}`);
    activeContainers.delete(sessionId);

    // 1. Emulate Ctrl+C by sending SIGINT to the main process inside the container
    await container.kill({ signal: 'SIGINT' }).catch(() => {});
    
    // Give the container 1.5 seconds to handle the signal and cleanup gracefully
    await new Promise(resolve => setTimeout(resolve, 1500));

    // 2. Force stop and remove container to ensure complete sandbox isolation and cleanup
    await container.stop({ t: 1 }).catch(() => {});
    await container.remove({ force: true }).catch(() => {});
    return true;
  } catch (err: any) {
    console.error(`[Docker] Error stopping container for session ${sessionId}:`, err.message);
    return false;
  }
}
