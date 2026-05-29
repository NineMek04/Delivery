import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule],
  template: `
    <aside class="sidebar">
      <div class="cluster-card">
        <div class="cluster-icon">💾</div>
        <div class="cluster-info">
          <span class="cluster-name">CLUSTER_01</span>
          <span class="cluster-version">v4.2.0-stable</span>
        </div>
      </div>

      <div class="sidebar-section">
        <label class="section-title">Suites</label>
        <ul class="menu-list">
          <li class="menu-item" (click)="onSelectSuite('csharp')" [class.active]="activeSuite === 'csharp'">
            <span class="item-icon">🛡️</span> Backend Integration (.NET)
          </li>
          <li class="menu-item" (click)="onSelectSuite('python')" [class.active]="activeSuite === 'python'">
            <span class="item-icon">🧠</span> AI Engine (Python)
          </li>
          <li class="menu-item" (click)="onSelectSuite('load')" [class.active]="activeSuite === 'load'">
            <span class="item-icon">⚡</span> Load Testing (Node.js)
          </li>
          <li class="menu-item" (click)="onSelectSuite('simulator')" [class.active]="activeSuite === 'simulator'">
            <span class="item-icon">🚀</span> E2E Simulator
          </li>
        </ul>
      </div>

      <div class="sidebar-footer">
        <label class="section-title">System Health</label>
        <div class="health-indicators">
          <div class="health-item">
            <span class="status-dot green"></span>
            <span class="health-label">Redis</span>
            <span class="health-status">OK</span>
          </div>
          <div class="health-item">
            <span class="status-dot blue"></span>
            <span class="health-label">API</span>
            <span class="health-status">Connected</span>
          </div>
          <div class="health-item">
            <span class="status-dot gray"></span>
            <span class="health-label">Worker</span>
            <span class="health-status">Idle</span>
          </div>
        </div>
      </div>
    </aside>
  `,
  styles: [`
    .sidebar {
      width: 240px;
      background-color: #0b0e17;
      border-right: 1px solid #21262d;
      display: flex;
      flex-direction: column;
      padding: 16px;
      height: 100%;
      overflow-y: auto;

      .cluster-card {
        background-color: #161b22;
        border: 1px solid #30363d;
        border-radius: 8px;
        padding: 12px;
        display: flex;
        align-items: center;
        gap: 12px;
        margin-bottom: 24px;

        .cluster-icon {
          font-size: 20px;
          color: #58a6ff;
        }

        .cluster-info {
          display: flex;
          flex-direction: column;

          .cluster-name {
            color: #f0f6fc;
            font-weight: 700;
            font-size: 13px;
            letter-spacing: 0.5px;
          }

          .cluster-version {
            color: #8b949e;
            font-size: 10px;
          }
        }
      }

      .sidebar-section {
        margin-bottom: 24px;

        .section-title {
          color: #8b949e;
          font-size: 9px;
          font-weight: 700;
          text-transform: uppercase;
          letter-spacing: 1.5px;
          display: block;
          margin-bottom: 8px;
          padding-left: 8px;
        }

        .menu-list {
          list-style: none;
          padding: 0;
          margin: 0;
          display: flex;
          flex-direction: column;
          gap: 4px;

          .menu-item {
            color: #c9d1d9;
            font-size: 12px;
            font-weight: 500;
            padding: 8px 12px;
            border-radius: 6px;
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 10px;
            transition: all 0.2s;

            .item-icon {
              color: #8b949e;
              font-size: 13px;
              width: 16px;
              text-align: center;
            }

            &:hover {
              background-color: rgba(255, 255, 255, 0.03);
              color: #f0f6fc;
              
              .item-icon {
                color: #c9d1d9;
              }
            }

            &.active {
              background-color: rgba(88, 166, 255, 0.1);
              color: #58a6ff;
              font-weight: 600;

              .item-icon {
                color: #58a6ff;
              }
            }
          }
        }
      }

      .sidebar-footer {
        margin-top: auto;
        border-top: 1px solid #21262d;
        padding-top: 16px;

        .health-indicators {
          display: flex;
          flex-direction: column;
          gap: 10px;
          padding-left: 8px;

          .health-item {
            display: flex;
            align-items: center;
            font-size: 11px;

            .status-dot {
              width: 6px;
              height: 6px;
              border-radius: 50%;
              margin-right: 8px;

              &.green {
                background-color: #3fb950;
                box-shadow: 0 0 6px #3fb950;
              }

              &.blue {
                background-color: #00e5ff;
                box-shadow: 0 0 6px #00e5ff;
              }

              &.gray {
                background-color: #8b949e;
              }
            }

            .health-label {
              color: #8b949e;
              flex: 1;
            }

            .health-status {
              color: #f0f6fc;
              font-weight: 600;
            }
          }
        }
      }
    }
  `]
})
export class SidebarComponent {
  @Input() activeSuite: string = 'overall';
  @Output() suiteSelected = new EventEmitter<'overall' | 'csharp' | 'python' | 'load' | 'simulator'>();

  onSelectSuite(suite: 'overall' | 'csharp' | 'python' | 'load' | 'simulator') {
    this.suiteSelected.emit(suite);
  }
}
