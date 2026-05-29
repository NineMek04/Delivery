import { Component, ElementRef, ViewChild, Input, OnDestroy, OnInit, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';

@Component({
  selector: 'app-live-terminal',
  standalone: true,
  imports: [CommonModule],
  template: `<div #terminalContainer class="terminal-container"></div>`,
  styles: [`
    .terminal-container {
      width: 100%;
      height: 100%;
      background: #090c10;
      border: 1px solid hsla(190, 100%, 50%, 0.15);
      box-shadow: inset 0 0 15px rgba(0, 0, 0, 0.8), 0 0 10px rgba(0, 229, 255, 0.03);
      border-radius: 8px;
      padding: 6px;
      overflow: hidden;
      position: relative;
    }
  `]
})
export class LiveTerminalComponent implements OnInit, OnChanges, OnDestroy {
  @ViewChild('terminalContainer', { static: true }) terminalContainer!: ElementRef;
  @Input() logs = '';

  private terminal!: Terminal;
  private fitAddon!: FitAddon;
  private resizeObserver!: ResizeObserver;

  ngOnInit() {
    this.initTerminal();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['logs'] && this.terminal) {
      const currentLogs = changes['logs'].currentValue || '';
      const previousLogs = changes['logs'].previousValue || '';
      
      // If logs are completely different or cleared, reset
      if (!currentLogs) {
        this.terminal.clear();
      } else if (currentLogs.length < previousLogs.length) {
        this.terminal.clear();
        this.writeLogs(currentLogs);
      } else {
        // Append only new logs
        const newLogs = currentLogs.substring(previousLogs.length);
        this.writeLogs(newLogs);
      }
    }
  }

  private initTerminal() {
    this.terminal = new Terminal({
      cursorBlink: true,
      fontSize: 13,
      fontFamily: "'Fira Code', 'Courier New', monospace",
      theme: {
        background: '#090c10',
        foreground: '#e6edf3',
        cursor: '#00e5ff',
        selectionBackground: 'rgba(0, 229, 255, 0.3)',
        black: '#0d1117',
        red: '#ff7b72',
        green: '#3fb950',
        yellow: '#d29922',
        blue: '#58a6ff',
        magenta: '#bc8cff',
        cyan: '#00e5ff',
        white: '#ffffff',
      },
      convertEol: true,
      scrollback: 10000,
    });

    this.fitAddon = new FitAddon();
    this.terminal.loadAddon(this.fitAddon);
    this.terminal.open(this.terminalContainer.nativeElement);
    this.fitAddon.fit();

    // Setup auto-resize
    this.resizeObserver = new ResizeObserver(() => {
      setTimeout(() => {
        if (this.fitAddon) {
          try {
            this.fitAddon.fit();
          } catch (e) {}
        }
      }, 50);
    });
    this.resizeObserver.observe(this.terminalContainer.nativeElement);

    // Initial message
    this.terminal.writeln('\x1b[1;36m[Orchestrator Terminal Initialized]\x1b[0m Ready for test executions...');
    
    if (this.logs) {
      this.writeLogs(this.logs);
    }
  }

  private writeLogs(text: string) {
    // Clean and properly format lines for terminal
    const lines = text.split('\n');
    lines.forEach((line, index) => {
      if (index === lines.length - 1) {
        this.terminal.write(line);
      } else {
        this.terminal.writeln(line);
      }
    });
  }

  ngOnDestroy() {
    if (this.resizeObserver) {
      this.resizeObserver.disconnect();
    }
    if (this.terminal) {
      this.terminal.dispose();
    }
  }
}
