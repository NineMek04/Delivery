import { Component, Input, Output, EventEmitter, ContentChild, TemplateRef, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule, Search, RefreshCcw, AlertTriangle, ChevronUp, ChevronDown, ChevronsUpDown } from 'lucide-angular';
import { Subject, Subscription } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';

export interface TableColumn {
  header: string;
  field: string;
  isSortable?: boolean;
  templateRef?: TemplateRef<any>;
}

@Component({
  selector: 'app-data-table',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  templateUrl: './data-table.component.html',
  styleUrl: './data-table.component.scss'
})
export class DataTableComponent implements OnInit, OnDestroy {
  readonly searchIcon = Search;
  readonly refreshIcon = RefreshCcw;
  readonly errorIcon = AlertTriangle;
  readonly ChevronUpIcon = ChevronUp;
  readonly ChevronDownIcon = ChevronDown;
  readonly ChevronsUpDownIcon = ChevronsUpDown;
  readonly Math = Math;

  @Input() title: string = 'Data Table';
  @Input() columns: TableColumn[] = [];
  @Input() data: any[] = [];
  @Input() isLoading: boolean = false;
  @Input() hasError: boolean = false;
  @Input() hasActions: boolean = false;
  
  @Input() totalCount: number = 0;
  @Input() currentPage: number = 1;
  @Input() pageSize: number = 20;
  @Input() searchQuery: string = '';

  @Output() pageChange = new EventEmitter<number>();
  @Output() search = new EventEmitter<string>();
  @Output() refresh = new EventEmitter<void>();
  @Output() retry = new EventEmitter<void>();
  @Output() sortChange = new EventEmitter<{field: string | null, dir: 'asc'|'desc'|null}>();

  @ContentChild('cellTemplate') cellTemplate?: TemplateRef<any>;
  @ContentChild('actionTemplate') actionTemplate?: TemplateRef<any>;

  private searchSubject = new Subject<string>();
  private searchSubscription?: Subscription;
  
  sortField: string | null = null;
  sortDir: 'asc' | 'desc' | null = null;

  ngOnInit(): void {
    this.searchSubscription = this.searchSubject.pipe(
      debounceTime(400),
      distinctUntilChanged()
    ).subscribe(query => {
      this.search.emit(query);
    });
  }

  ngOnDestroy(): void {
    if (this.searchSubscription) {
      this.searchSubscription.unsubscribe();
    }
  }

  onPageChange(newPage: number) {
    if (newPage >= 1 && newPage <= Math.ceil(this.totalCount / this.pageSize)) {
      this.pageChange.emit(newPage);
    }
  }

  onSearchChange(query: string) {
    this.searchQuery = query;
    this.searchSubject.next(query);
  }

  onSort(field: string) {
    if (this.sortField === field) {
      this.sortDir = this.sortDir === 'asc' ? 'desc' : (this.sortDir === 'desc' ? null : 'asc');
      if (!this.sortDir) this.sortField = null;
    } else {
      this.sortField = field;
      this.sortDir = 'asc';
    }
    this.sortChange.emit({ field: this.sortField, dir: this.sortDir });
  }

  // Generates page numbers for professional pagination e.g., 1 2 3 ... 10
  getPages(): (number | string)[] {
    const totalPages = Math.ceil(this.totalCount / this.pageSize);
    if (totalPages <= 1) return [];
    
    const pages: (number | string)[] = [];
    const current = this.currentPage;
    
    if (totalPages <= 7) {
      for (let i = 1; i <= totalPages; i++) {
        pages.push(i);
      }
    } else {
      pages.push(1);
      
      if (current > 3) {
        pages.push('...');
      }
      
      const start = Math.max(2, current - 1);
      const end = Math.min(totalPages - 1, current + 1);
      
      for (let i = start; i <= end; i++) {
        pages.push(i);
      }
      
      if (current < totalPages - 2) {
        pages.push('...');
      }
      
      pages.push(totalPages);
    }
    
    return pages;
  }
}

