import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';

interface SimulationPlugin {
  id: string;
  name: string;
  description: string;
  icon: string;
  active: boolean;
  intensity: number; // 0-100%
}

interface SimulatedRider {
  id: string;
  name: string;
  status: 'IDLE' | 'DELIVERING' | 'OFFLINE';
  x: number; // 0-100% on grid
  y: number; // 0-100% on grid
  color: string;
}

@Component({
  selector: 'app-simulator-host',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  template: `
    <div class="simulator-panel glass-card">
      <div class="sim-header">
        <div class="title-area">
          <span class="pulse-dot"></span>
          <h3>🚀 Core E2E Simulator Engine</h3>
        </div>
        <div class="controls-area">
          <button (click)="toggleSimulation()" [class.running]="isSimulating" class="action-btn">
            {{ isSimulating ? '⏸️ Pause Engine' : '▶️ Start Playback' }}
          </button>
          <button (click)="resetSimulation()" class="action-btn secondary">🔄 Reset</button>
        </div>
      </div>

      <div class="sim-body">
        <!-- 1. The Dynamic Grid Map (Wow Grid System) -->
        <div class="map-grid-container">
          <div class="map-grid">
            <div class="grid-overlay"></div>
            
            <!-- Nodes and Routes -->
            <svg class="routes-svg">
              <!-- Grid lines simulating roads -->
              <line x1="10%" y1="10%" x2="90%" y2="10%" stroke="rgba(255,255,255,0.05)" stroke-width="2"/>
              <line x1="10%" y1="50%" x2="90%" y2="50%" stroke="rgba(255,255,255,0.05)" stroke-width="2"/>
              <line x1="10%" y1="90%" x2="90%" y2="90%" stroke="rgba(255,255,255,0.05)" stroke-width="2"/>
              <line x1="10%" y1="10%" x2="10%" y2="90%" stroke="rgba(255,255,255,0.05)" stroke-width="2"/>
              <line x1="50%" y1="10%" x2="50%" y2="90%" stroke="rgba(255,255,255,0.05)" stroke-width="2"/>
              <line x1="90%" y1="10%" x2="90%" y2="90%" stroke="rgba(255,255,255,0.05)" stroke-width="2"/>

              <!-- Simulated Active Route -->
              <path *ngIf="isSimulating" d="M 100,100 L 300,100 L 300,300" 
                    fill="none" stroke="var(--color-primary)" stroke-width="2" 
                    stroke-dasharray="8,4" class="path-animate"/>
            </svg>

            <!-- Store Hub -->
            <div class="hub-marker store-hub" style="left: 10%; top: 10%;">
              <span class="label">🏢 Main Hub</span>
            </div>

            <!-- Customer Destination -->
            <div class="hub-marker customer-dest" style="left: 50%; top: 50%;">
              <span class="label">🏠 Client</span>
            </div>

            <!-- Animated Riders -->
            <div *ngFor="let rider of riders" 
                 class="rider-marker" 
                 [style.left.%]="rider.x" 
                 [style.top.%]="rider.y"
                 [style.background-color]="rider.color"
                 [class.active]="rider.status === 'DELIVERING'">
              <span class="marker-dot"></span>
              <span class="rider-tooltip">{{ rider.name }} ({{ rider.status }})</span>
            </div>
          </div>
          
          <div class="playback-bar">
            <span>Progress: {{ playbackProgress }}%</span>
            <div class="progress-track">
              <div class="progress-fill" [style.width.%]="playbackProgress"></div>
            </div>
          </div>
        </div>

        <!-- 2. Simulation Plugin Architecture Sidebar -->
        <div class="plugins-sidebar">
          <h4>🧪 Simulation Plugins (Chaos Layer)</h4>
          <p class="muted">Inject variables and test real-time failovers.</p>

          <div class="plugins-list">
            <div *ngFor="let plugin of plugins" class="plugin-card" [class.active]="plugin.active">
              <div class="plugin-header">
                <span class="icon">{{ plugin.icon }}</span>
                <div class="plugin-info">
                  <h5>{{ plugin.name }}</h5>
                  <span class="desc">{{ plugin.description }}</span>
                </div>
                <input type="checkbox" [(ngModel)]="plugin.active" (change)="onPluginToggle(plugin)">
              </div>
              <div class="plugin-slider" *ngIf="plugin.active">
                <span class="slider-label">Intensity: {{ plugin.intensity }}%</span>
                <input type="range" min="0" max="100" [(ngModel)]="plugin.intensity" class="range-slider">
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .simulator-panel {
      display: flex;
      flex-direction: column;
      gap: 1.25rem;
      background: var(--bg-card);
      border: 1px solid var(--border-glass);
      padding: 1.5rem;
      border-radius: 16px;
    }

    .sim-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      border-bottom: 1px solid var(--border-glass);
      padding-bottom: 0.75rem;

      .title-area {
        display: flex;
        align-items: center;
        gap: 0.5rem;

        h3 {
          font-weight: 600;
          letter-spacing: 0.5px;
        }

        .pulse-dot {
          width: 8px;
          height: 8px;
          border-radius: 50%;
          background-color: var(--color-success);
          box-shadow: 0 0 8px var(--color-success);
          animation: pulse 1.5s infinite;
        }
      }

      .controls-area {
        display: flex;
        gap: 0.5rem;
      }
    }

    .sim-body {
      display: grid;
      grid-template-columns: 1.6fr 1fr;
      gap: 1.5rem;
    }

    @media (max-width: 992px) {
      .sim-body {
        grid-template-columns: 1fr;
      }
    }

    .map-grid-container {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }

    .map-grid {
      position: relative;
      height: 320px;
      background: #06090e;
      border: 1px solid var(--border-glass);
      border-radius: 12px;
      overflow: hidden;

      .grid-overlay {
        position: absolute;
        top: 0; left: 0; right: 0; bottom: 0;
        background-size: 20px 20px;
        background-image: 
          linear-gradient(to right, rgba(255, 255, 255, 0.02) 1px, transparent 1px),
          linear-gradient(to bottom, rgba(255, 255, 255, 0.02) 1px, transparent 1px);
      }

      .routes-svg {
        position: absolute;
        width: 100%;
        height: 100%;
        top: 0; left: 0;
      }
    }

    .hub-marker {
      position: absolute;
      padding: 4px 8px;
      border-radius: 4px;
      font-size: 10px;
      font-weight: bold;
      transform: translate(-50%, -50%);

      &.store-hub {
        background: rgba(0, 229, 255, 0.2);
        border: 1px solid var(--color-primary);
        color: var(--color-primary);
      }

      &.customer-dest {
        background: rgba(255, 0, 191, 0.2);
        border: 1px solid var(--color-secondary);
        color: var(--color-secondary);
      }
    }

    .rider-marker {
      position: absolute;
      width: 14px;
      height: 14px;
      border-radius: 50%;
      border: 2px solid #fff;
      transform: translate(-50%, -50%);
      cursor: pointer;
      transition: left 0.1s linear, top 0.1s linear;

      .marker-dot {
        position: absolute;
        top: 50%; left: 50%;
        width: 6px; height: 6px;
        background: #fff;
        border-radius: 50%;
        transform: translate(-50%, -50%);
      }

      .rider-tooltip {
        visibility: hidden;
        position: absolute;
        background: #000;
        color: #fff;
        text-align: center;
        padding: 4px 8px;
        border-radius: 4px;
        font-size: 9px;
        white-space: nowrap;
        bottom: 125%;
        left: 50%;
        transform: translateX(-50%);
        opacity: 0;
        transition: opacity 0.2s;
        z-index: 10;
        border: 1px solid var(--border-glass);
      }

      &:hover .rider-tooltip {
        visibility: visible;
        opacity: 1;
      }

      &.active {
        box-shadow: 0 0 10px #fff;
      }
    }

    .playback-bar {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      font-size: 11px;
      color: var(--color-muted);

      .progress-track {
        flex: 1;
        height: 6px;
        background: rgba(255, 255, 255, 0.05);
        border-radius: 3px;
        overflow: hidden;
      }

      .progress-fill {
        height: 100%;
        background: linear-gradient(90deg, var(--color-primary), var(--color-secondary));
        transition: width 0.1s linear;
      }
    }

    .plugins-sidebar {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;

      h4 {
        font-size: 14px;
        font-weight: 600;
      }

      .plugins-list {
        display: flex;
        flex-direction: column;
        gap: 0.75rem;
        margin-top: 0.5rem;
      }
    }

    .plugin-card {
      background: rgba(255, 255, 255, 0.02);
      border: 1px solid var(--border-glass);
      border-radius: 8px;
      padding: 0.75rem;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      transition: var(--transition-smooth);

      &.active {
        border-color: hsla(315, 100%, 55%, 0.3);
        background: rgba(255, 0, 191, 0.02);
      }

      .plugin-header {
        display: flex;
        align-items: center;
        gap: 0.75rem;

        .icon {
          font-size: 18px;
        }

        .plugin-info {
          flex: 1;
          display: flex;
          flex-direction: column;
          
          h5 {
            font-size: 12px;
            font-weight: bold;
          }
          
          .desc {
            font-size: 10px;
            color: var(--color-muted);
          }
        }
      }

      .plugin-slider {
        display: flex;
        flex-direction: column;
        gap: 0.25rem;
        font-size: 10px;
        color: var(--color-muted);

        .range-slider {
          -webkit-appearance: none;
          width: 100%;
          height: 4px;
          border-radius: 2px;
          background: rgba(255, 255, 255, 0.1);
          outline: none;
          
          &::-webkit-slider-thumb {
            -webkit-appearance: none;
            width: 10px;
            height: 10px;
            border-radius: 50%;
            background: var(--color-secondary);
            cursor: pointer;
          }
        }
      }
    }

    .action-btn {
      padding: 0.5rem 1rem;
      border: 1px solid var(--color-primary);
      background: rgba(0, 229, 255, 0.05);
      color: var(--color-primary);
      border-radius: 8px;
      cursor: pointer;
      font-size: 12px;
      font-family: var(--font-primary);
      font-weight: 600;
      transition: var(--transition-smooth);

      &:hover {
        background: var(--color-primary);
        color: #000;
        box-shadow: var(--shadow-neon-cyan);
      }

      &.running {
        border-color: var(--color-warning);
        color: var(--color-warning);
        background: rgba(242, 201, 76, 0.05);

        &:hover {
          background: var(--color-warning);
          color: #000;
        }
      }

      &.secondary {
        border-color: var(--border-glass);
        background: transparent;
        color: var(--color-muted);

        &:hover {
          background: rgba(255,255,255,0.05);
          color: #fff;
        }
      }
    }

    .muted {
      font-size: 11px;
      color: var(--color-muted);
    }

    @keyframes pulse {
      0% { transform: scale(1); opacity: 0.8; }
      50% { transform: scale(1.2); opacity: 1; box-shadow: 0 0 12px var(--color-success); }
      100% { transform: scale(1); opacity: 0.8; }
    }

    @keyframes path-animate {
      to {
        stroke-dashoffset: -20;
      }
    }

    .path-animate {
      animation: path-animate 2s linear infinite;
    }
  `]
})
export class SimulatorHostComponent implements OnInit, OnDestroy {
  isSimulating = false;
  playbackProgress = 0;
  private intervalHandle: any = null;

  riders: SimulatedRider[] = [
    { id: '1', name: 'Rider #104 (Somchai)', status: 'IDLE', x: 10, y: 10, color: 'hsl(190, 100%, 50%)' },
    { id: '2', name: 'Rider #205 (Somsak)', status: 'OFFLINE', x: 50, y: 10, color: 'hsl(230, 15%, 45%)' },
    { id: '3', name: 'Rider #309 (Wichai)', status: 'IDLE', x: 90, y: 90, color: 'hsl(145, 90%, 50%)' }
  ];

  plugins: SimulationPlugin[] = [
    { id: 'jitter', name: 'GPS Jitter Simulator', description: 'Simulate coordinate drift in urban environments', icon: '📡', active: false, intensity: 25 },
    { id: 'chaos', name: 'Traffic Gridlock Chaos', description: 'Impose severe route routing delays', icon: '🚦', active: false, intensity: 50 },
    { id: 'fake', name: 'Fake Rider Emulator', description: 'Inject phantom GPS locations', icon: '🚲', active: false, intensity: 10 },
    { id: 'loss', name: 'Telemetry Packet Loss', description: 'Mimic cellular dropout under heavy tunnels', icon: '📶', active: false, intensity: 30 }
  ];

  ngOnInit() {}

  toggleSimulation() {
    this.isSimulating = !this.isSimulating;
    if (this.isSimulating) {
      this.riders[0].status = 'DELIVERING';
      this.riders[2].status = 'DELIVERING';
      this.intervalHandle = setInterval(() => {
        this.playbackProgress = (this.playbackProgress + 1) % 101;
        
        // Move SOMCHAI along the L path (x 10->50, then y 10->50)
        if (this.playbackProgress <= 50) {
          this.riders[0].x = 10 + (this.playbackProgress * 0.8);
          this.riders[0].y = 10;
        } else {
          this.riders[0].x = 50;
          this.riders[0].y = 10 + ((this.playbackProgress - 50) * 0.8);
        }

        // Wichai moves randomly on jitter
        const jitterPlugin = this.plugins.find(p => p.id === 'jitter');
        if (jitterPlugin?.active) {
          const maxShift = (jitterPlugin.intensity / 100) * 3;
          this.riders[2].x = Math.max(80, Math.min(100, this.riders[2].x + (Math.random() - 0.5) * maxShift));
          this.riders[2].y = Math.max(80, Math.min(100, this.riders[2].y + (Math.random() - 0.5) * maxShift));
        }

      }, 100);
    } else {
      if (this.intervalHandle) {
        clearInterval(this.intervalHandle);
      }
    }
  }

  resetSimulation() {
    this.isSimulating = false;
    if (this.intervalHandle) {
      clearInterval(this.intervalHandle);
    }
    this.playbackProgress = 0;
    this.riders[0] = { id: '1', name: 'Rider #104 (Somchai)', status: 'IDLE', x: 10, y: 10, color: 'hsl(190, 100%, 50%)' };
    this.riders[2] = { id: '3', name: 'Rider #309 (Wichai)', status: 'IDLE', x: 90, y: 90, color: 'hsl(145, 90%, 50%)' };
  }

  onPluginToggle(plugin: SimulationPlugin) {
    console.log(`[Plugin Architecture] Plugin ${plugin.id} toggled: ${plugin.active}`);
  }

  ngOnDestroy() {
    if (this.intervalHandle) {
      clearInterval(this.intervalHandle);
    }
  }
}
