/**
 * @license
 * SPDX-License-Identifier: Apache-2.0
 */

import React, { useState } from 'react';
import { 
  LayoutDashboard, 
  Warehouse, 
  Truck, 
  BarChart3, 
  Settings, 
  Search, 
  Bell, 
  User,
  Plus,
  ArrowUpRight,
  ArrowDownRight,
  Package,
  Clock,
  AlertTriangle,
  Menu,
  ChevronRight
} from 'lucide-react';
import { motion, AnimatePresence } from 'motion/react';
import { 
  BarChart, 
  Bar, 
  XAxis, 
  YAxis, 
  CartesianGrid, 
  Tooltip, 
  ResponsiveContainer, 
  AreaChart, 
  Area,
  LineChart,
  Line
} from 'recharts';

// --- Types ---
type View = 'dashboard' | 'warehouse' | 'fleet' | 'analytics' | 'settings';

interface Vehicle {
  id: string;
  plate: string;
  model: string;
  status: 'active' | 'maintenance' | 'idle';
  fuel: number;
  lastService: string;
}

interface StatCardProps {
  title: string;
  value: string;
  change: string;
  isPositive: boolean;
  icon: React.ReactNode;
}

interface InventoryItem {
  id: string;
  name: string;
  category: string;
  stock: number;
  minStock: number;
  location: string;
  status: 'optimal' | 'low' | 'out';
}

interface Shipment {
  id: string;
  destination: string;
  vehicle: string;
  driver: string;
  status: 'pending' | 'in-transit' | 'delivered';
  eta: string;
}

// --- Mock Data ---
const INVENTORY_DATA: InventoryItem[] = [
  { id: 'SKU-001', name: 'Industrial Motor v2', category: 'Machinery', stock: 45, minStock: 20, location: 'Zone A-12', status: 'optimal' },
  { id: 'SKU-002', name: 'Aluminium Sheets', category: 'Raw Material', stock: 12, minStock: 50, location: 'Zone B-05', status: 'low' },
  { id: 'SKU-003', name: 'Hydraulic Fluid', category: 'Chemicals', stock: 0, minStock: 10, location: 'Storage C', status: 'out' },
  { id: 'SKU-004', name: 'Power Cables (100m)', category: 'Electrical', stock: 88, minStock: 30, location: 'Zone A-08', status: 'optimal' },
];

const SHIPMENTS: Shipment[] = [
  { id: 'SHP-928', destination: 'Distribution Center West', vehicle: 'Volvo FH16', driver: 'Somsak R.', status: 'in-transit', eta: '14:30' },
  { id: 'SHP-929', destination: 'City Port Terminal', vehicle: 'Scania R500', driver: 'Vichai P.', status: 'pending', eta: '16:45' },
  { id: 'SHP-930', destination: 'Northern Hub', vehicle: 'Isuzu Giga', driver: 'Anan K.', status: 'delivered', eta: '11:15' },
];

const VEHICLES: Vehicle[] = [
  { id: 'V-101', plate: '77-8899 BKK', model: 'Volvo FH16', status: 'active', fuel: 75, lastService: '2024-03-10' },
  { id: 'V-102', plate: '11-2233 BKK', model: 'Scania R500', status: 'idle', fuel: 40, lastService: '2024-04-02' },
  { id: 'V-103', plate: '44-5566 BKK', model: 'Isuzu Giga', status: 'maintenance', fuel: 15, lastService: '2023-12-15' },
];

const ANALYTICS_DATA = [
  { name: 'Mon', shipments: 45, stock: 400 },
  { name: 'Tue', shipments: 52, stock: 380 },
  { name: 'Wed', shipments: 38, stock: 420 },
  { name: 'Thu', shipments: 65, stock: 390 },
  { name: 'Fri', shipments: 48, stock: 350 },
  { name: 'Sat', shipments: 24, stock: 360 },
  { name: 'Sun', shipments: 12, stock: 370 },
];

// --- Components ---

const SidebarItem = ({ 
  icon: Icon, 
  label, 
  active, 
  onClick 
}: { 
  icon: any, 
  label: string, 
  active: boolean, 
  onClick: () => void 
}) => (
  <button
    onClick={onClick}
    className={`w-full flex items-center gap-3 px-4 py-2.5 rounded transition-all duration-200 ${
      active 
        ? 'bg-surface-line text-brand-primary shadow-inner' 
        : 'text-text-muted hover:bg-surface-line/50 hover:text-text-primary'
    }`}
  >
    <Icon size={18} strokeWidth={active ? 2.5 : 2} />
    <span className="font-medium text-xs uppercase tracking-widest">{label}</span>
    {active && (
      <motion.div 
        layoutId="activeTab"
        className="ml-auto w-1 h-3 rounded-full bg-brand-primary"
      />
    )}
  </button>
);

const StatCard = ({ title, value, change, isPositive, icon }: StatCardProps) => (
  <div className="bg-surface-card p-5 rounded-none border border-surface-line shadow-sm hover:border-brand-primary/30 transition-all group">
    <div className="flex justify-between items-start mb-4">
      <div className="p-2 bg-surface-line rounded-none text-brand-primary group-hover:scale-110 transition-transform">
        {icon}
      </div>
      <div className={`flex items-center gap-1 text-[10px] font-mono font-bold ${isPositive ? 'text-brand-primary' : 'text-status-error'}`}>
        {isPositive ? <ArrowUpRight size={12} /> : <ArrowDownRight size={12} />}
        {change}
      </div>
    </div>
    <div className="space-y-1">
      <p className="text-text-muted text-[10px] uppercase tracking-[0.2em] font-bold">{title}</p>
      <p className="text-2xl font-bold font-mono tracking-tighter text-text-primary">{value}</p>
    </div>
  </div>
);

const StatusBadge = ({ status }: { status: string }) => {
  const styles = {
    optimal: 'text-brand-primary border-brand-primary/50 bg-brand-primary/5',
    low: 'text-status-warning border-status-warning/50 bg-status-warning/5',
    out: 'text-status-error border-status-error/50 bg-status-error/5',
    'in-transit': 'text-blue-400 border-blue-400/50 bg-blue-400/5',
    pending: 'text-text-muted border-text-muted/50 bg-text-muted/5',
    delivered: 'text-brand-primary border-brand-primary/50 bg-brand-primary/5',
  };
  
  const style = styles[status as keyof typeof styles] || styles.pending;
  
  return (
    <span className={`px-2.5 py-1 rounded-full text-[10px] font-bold uppercase tracking-wider border ${style}`}>
      {status}
    </span>
  );
};

export default function App() {
  const [activeView, setActiveView] = useState<View>('dashboard');
  const [isSidebarOpen, setIsSidebarOpen] = useState(true);
  const [isShipmentModalOpen, setIsShipmentModalOpen] = useState(false);

  return (
    <div className="flex h-screen overflow-hidden selection:bg-brand-primary selection:text-black">
      {/* Sidebar */}
      <motion.aside 
        initial={false}
        animate={{ width: isSidebarOpen ? 240 : 80 }}
        className="bg-surface-card border-r border-surface-line flex flex-col z-20"
      >
        <div className="p-6 flex items-center gap-3 h-20 border-b border-surface-line">
          <div className="w-8 h-8 bg-brand-primary rounded-sm flex items-center justify-center shrink-0 shadow-[0_0_15px_rgba(0,255,102,0.3)]">
            <Truck className="text-black" size={20} />
          </div>
          {isSidebarOpen && (
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              className="flex flex-col overflow-hidden"
            >
              <h1 className="font-bold text-sm leading-tight truncate tracking-tighter">SFWI_PLATFORM</h1>
              <span className="text-[9px] font-bold text-brand-primary uppercase tracking-[0.3em]">Operational</span>
            </motion.div>
          )}
        </div>

        <div className="flex-1 overflow-y-auto py-6 px-4 space-y-8">
          <section className="space-y-3">
            {isSidebarOpen && <span className="px-4 text-[10px] font-bold text-text-muted uppercase tracking-[0.2em]">Navigation</span>}
            <nav className="space-y-1">
              <SidebarItem 
                icon={LayoutDashboard} 
                label={isSidebarOpen ? "Dashboard" : ""} 
                active={activeView === 'dashboard'} 
                onClick={() => setActiveView('dashboard')} 
              />
              <SidebarItem 
                icon={Warehouse} 
                label={isSidebarOpen ? "Warehouse" : ""} 
                active={activeView === 'warehouse'} 
                onClick={() => setActiveView('warehouse')} 
              />
              <SidebarItem 
                icon={Truck} 
                label={isSidebarOpen ? "Fleet" : ""} 
                active={activeView === 'fleet'} 
                onClick={() => setActiveView('fleet')} 
              />
              <SidebarItem 
                icon={BarChart3} 
                label={isSidebarOpen ? "Analytics" : ""} 
                active={activeView === 'analytics'} 
                onClick={() => setActiveView('analytics')} 
              />
            </nav> section
          </section>
        </div>

        <div className="p-4 border-t border-surface-line bg-black/20">
          <SidebarItem 
            icon={Settings} 
            label={isSidebarOpen ? "Settings" : ""} 
            active={activeView === 'settings'} 
            onClick={() => setActiveView('settings')} 
          />
        </div>
      </motion.aside>

      {/* Main Content */}
      <main className="flex-1 flex flex-col h-full bg-surface-bg overflow-hidden relative">
        <div className="absolute inset-0 technical-grid opacity-30 pointer-events-none"></div>
        
        {/* Header */}
        <header className="h-16 bg-surface-card/80 backdrop-blur-md border-b border-surface-line px-8 flex items-center justify-between sticky top-0 z-10 shrink-0">
          <div className="flex items-center gap-6">
            <button 
              onClick={() => setIsSidebarOpen(!isSidebarOpen)}
              className="p-1.5 hover:bg-surface-line rounded transition-colors text-text-muted hover:text-brand-primary"
            >
              <Menu size={18} />
            </button>
            <div className="flex items-center gap-2 text-xs font-mono text-text-muted uppercase tracking-widest hidden md:flex">
              <span className="text-brand-primary">COMMAND</span>
              <span className="opacity-50">/</span>
              <div className="bg-surface-line px-3 py-1.5 rounded flex items-center gap-3 min-w-[300px]">
                <Search size={14} className="text-text-muted" />
                <input 
                  type="text" 
                  placeholder="search_payload_id..."
                  className="bg-transparent border-none text-[11px] w-full focus:outline-none placeholder:text-text-muted/30"
                />
              </div>
            </div>
          </div>

          <div className="flex items-center gap-6">
            <div className="flex items-center gap-4">
              <button className="text-text-muted hover:text-brand-primary transition-colors relative">
                <Bell size={18} />
                <span className="absolute -top-1 -right-1 w-2 h-2 bg-status-error rounded-full ring-2 ring-surface-card"></span>
              </button>
            </div>
            <div className="h-8 w-px bg-surface-line"></div>
            <div className="flex items-center gap-3 pl-2 group">
              <div className="text-right">
                <p className="text-[11px] font-bold leading-tight text-text-primary uppercase">Suphachai K.</p>
                <p className="text-[9px] uppercase font-bold text-text-muted tracking-widest">SYSTEM_ADMIN</p>
              </div>
              <div className="w-8 h-8 bg-surface-line rounded-full border border-surface-line flex items-center justify-center group-hover:border-brand-primary transition-all overflow-hidden shrink-0">
                <User size={16} className="text-text-muted" />
              </div>
            </div>
          </div>
        </header>

        {/* Scrollable Content Area */}
        <div className="flex-1 overflow-y-auto p-8 custom-scrollbar relative z-0">
          <AnimatePresence mode="wait">
            <motion.div
              key={activeView}
              initial={{ opacity: 0, x: 10 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: -10 }}
              transition={{ duration: 0.2 }}
            >
              {activeView === 'dashboard' && <DashboardView onCreateShipment={() => setIsShipmentModalOpen(true)} />}
              {activeView === 'warehouse' && <WarehouseView />}
              {activeView === 'fleet' && <FleetView />}
              {activeView === 'analytics' && <AnalyticsView />}
            </motion.div>
          </AnimatePresence>

          {/* Shipment Creation Modal */}
          <AnimatePresence>
            {isShipmentModalOpen && (
              <motion.div 
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                className="fixed inset-0 bg-black/80 backdrop-blur-md z-50 flex items-center justify-center p-4 overflow-y-auto scrollbar-hide"
                onClick={() => setIsShipmentModalOpen(false)}
              >
                <motion.div 
                  initial={{ scale: 0.95, opacity: 0, y: 20 }}
                  animate={{ scale: 1, opacity: 1, y: 0 }}
                  exit={{ scale: 0.95, opacity: 0, y: 20 }}
                  className="bg-surface-card w-full max-w-2xl rounded border border-surface-line shadow-[0_0_50px_rgba(0,0,0,0.5)] overflow-hidden my-auto"
                  onClick={(e) => e.stopPropagation()}
                >
                  <div className="p-8 border-b border-surface-line flex justify-between items-center bg-white/[0.02]">
                    <div>
                      <h3 className="text-xl font-bold tracking-tight uppercase font-mono text-brand-primary">Shipment_Manifest_v1.0</h3>
                      <p className="text-text-muted text-[10px] font-bold uppercase tracking-[0.2em] mt-1">Operational Protocol: Create_New_Transit</p>
                    </div>
                    <button 
                      onClick={() => setIsShipmentModalOpen(false)}
                      className="p-2 hover:bg-surface-line rounded transition-colors text-text-muted hover:text-brand-primary"
                    >
                      <Plus className="rotate-45" size={24} />
                    </button>
                  </div>
                  
                  <div className="p-8 grid grid-cols-1 md:grid-cols-2 gap-8">
                    <div className="space-y-6">
                      <div className="space-y-2">
                        <label className="text-[10px] font-bold uppercase tracking-widest text-text-muted pl-1 font-mono">TARGET_DESTINATION</label>
                        <select className="w-full bg-surface-line border-none rounded p-3.5 text-xs font-bold text-text-primary focus:ring-1 focus:ring-brand-primary outline-none cursor-pointer hover:bg-surface-line/80 transition-colors">
                          <option>Distribution Center West</option>
                          <option>City Port Terminal</option>
                          <option>Northern Hub</option>
                        </select>
                      </div>
                      <div className="space-y-2">
                        <label className="text-[10px] font-bold uppercase tracking-widest text-text-muted pl-1 font-mono">ASSET_ASSIGNMENT</label>
                        <select className="w-full bg-surface-line border-none rounded p-3.5 text-xs font-bold text-text-primary focus:ring-1 focus:ring-brand-primary outline-none cursor-pointer hover:bg-surface-line/80 transition-colors">
                          {VEHICLES.map(v => (
                            <option key={v.id}>{v.model} // {v.plate}</option>
                          ))}
                        </select>
                      </div>
                    </div>

                    <div className="space-y-6">
                       <div className="space-y-2">
                        <label className="text-[10px] font-bold uppercase tracking-widest text-text-muted pl-1 font-mono">PRIORITY_LEVEL</label>
                        <div className="flex gap-2">
                          {['Normal', 'High', 'Critical'].map(p => (
                            <button key={p} className={`flex-1 py-3 rounded border text-[10px] font-bold uppercase tracking-widest transition-all ${p === 'High' ? 'bg-brand-primary text-black border-brand-primary shadow-[0_0_10px_rgba(0,255,102,0.3)]' : 'bg-surface-line border-transparent text-text-muted hover:text-text-primary'}`}>
                              {p}
                            </button>
                          ))}
                        </div>
                      </div>
                      <div className="space-y-2">
                        <label className="text-[10px] font-bold uppercase tracking-widest text-text-muted pl-1 font-mono">MISSION_LOG_NOTES</label>
                        <textarea 
                          placeholder="INPUT SPECIAL INSTRUCTIONS..."
                          className="w-full bg-surface-line border-none rounded p-3.5 text-xs font-mono text-text-primary focus:ring-1 focus:ring-brand-primary outline-none h-24 resize-none placeholder:text-text-muted/20"
                        ></textarea>
                      </div>
                    </div>
                  </div>

                  <div className="p-8 bg-black/40 border-t border-surface-line flex justify-end gap-4">
                    <button 
                      onClick={() => setIsShipmentModalOpen(false)}
                      className="px-6 py-3 rounded font-bold text-[11px] bg-transparent border border-surface-line text-text-muted hover:text-text-primary hover:bg-surface-line transition-all uppercase tracking-widest"
                    >
                      Abort_Process
                    </button>
                    <button className="px-8 py-3 rounded font-bold text-[11px] bg-brand-primary text-black shadow-lg shadow-brand-primary/20 hover:scale-105 active:scale-95 transition-all uppercase tracking-widest">
                      Commit_Manifest
                    </button>
                  </div>
                </motion.div>
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      </main>
    </div>
  );
}

function FleetView() {
  return (
    <div className="space-y-10">
      <div className="flex justify-between items-end">
        <div>
          <h2 className="text-3xl font-bold tracking-tight uppercase font-mono">Fleet_Operations</h2>
          <p className="text-text-muted font-medium text-sm tracking-wide">Vehicle health & telemetry monitoring cluster</p>
        </div>
        <button className="bg-brand-primary text-black px-6 py-3 rounded font-bold text-[11px] shadow-[0_0_20px_rgba(0,255,102,0.2)] hover:scale-105 active:scale-95 transition-all flex items-center gap-2 uppercase tracking-widest">
          <Truck size={16} />
          Register_New_Asset
        </button>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        {VEHICLES.map(v => (
          <div key={v.id} className="bg-surface-card p-6 rounded border border-surface-line shadow-sm space-y-6 hover:border-brand-primary/40 transition-colors group">
            <div className="flex justify-between items-start">
              <div className="flex items-center gap-3">
                <div className="w-12 h-12 bg-surface-line rounded flex items-center justify-center text-brand-primary group-hover:bg-brand-primary/10 transition-colors">
                  <Truck size={24} />
                </div>
                <div>
                  <p className="font-bold text-base leading-tight tracking-tight uppercase group-hover:text-brand-primary transition-colors">{v.model}</p>
                  <p className="text-[10px] font-bold text-text-muted uppercase tracking-[0.2em]">{v.plate}</p>
                </div>
              </div>
              <StatusBadge status={v.status} />
            </div>

            <div className="space-y-4">
              <div className="flex justify-between text-[10px] font-bold uppercase tracking-widest items-center">
                <span className="text-text-muted">FUEL_LEVEL</span>
                <span className="text-text-primary font-mono">{v.fuel}%</span>
              </div>
              <div className="h-1 bg-surface-line rounded-none overflow-hidden">
                <div 
                  className={`h-full transition-all duration-1000 ${v.fuel < 20 ? 'bg-status-error' : 'bg-brand-primary shadow-[0_0_10px_rgba(0,255,102,0.5)]'}`}
                  style={{ width: `${v.fuel}%` }}
                />
              </div>
            </div>

            <div className="grid grid-cols-2 gap-4 pt-4 border-t border-surface-line">
              <div>
                <p className="text-[9px] font-bold text-text-muted uppercase tracking-widest">ASSET_ID</p>
                <p className="text-xs font-mono font-bold mt-0.5 tracking-tight">{v.id}</p>
              </div>
              <div>
                <p className="text-[9px] font-bold text-text-muted uppercase tracking-widest">SERVICE_DATE</p>
                <p className="text-xs font-bold mt-0.5">{v.lastService}</p>
              </div>
            </div>
            
            <button className="w-full py-3 bg-surface-line text-text-primary rounded text-[10px] font-bold hover:bg-brand-primary hover:text-black transition-all uppercase tracking-[0.2em]">
              TELEMETRY_LOGS
            </button>
          </div>
        ))}
      </div>

      {/* Map Placeholder */}
      <div className="relative h-[450px] bg-black rounded border border-surface-line overflow-hidden shadow-2xl flex items-center justify-center group">
        <div className="absolute inset-0 bg-[#00FF66] opacity-[0.03] technical-grid"></div>
        <div className="absolute inset-0 bg-gradient-to-t from-black via-transparent to-transparent"></div>
        
        <div className="z-10 text-center space-y-6 max-w-lg px-8">
          <div className="w-20 h-20 bg-brand-primary/10 rounded-full flex items-center justify-center mx-auto animate-pulse border border-brand-primary/20">
             <ArrowUpRight className="text-brand-primary" size={40} />
          </div>
          <div className="space-y-2">
            <h3 className="font-bold text-2xl uppercase font-mono tracking-tighter text-text-primary">Real-time GPS Telemetry</h3>
            <p className="text-text-muted text-sm font-medium leading-relaxed uppercase tracking-wide opacity-80 underline underline-offset-8 decoration-brand-primary/30">Connect G-Grip Gateway to enable live tracking</p>
          </div>
          <div className="bg-surface-line/50 p-4 rounded border border-surface-line flex items-center justify-center gap-6">
            <div className="flex flex-col items-center">
               <span className="text-[9px] font-bold text-text-muted uppercase tracking-widest">Satellites</span>
               <span className="text-brand-primary font-mono text-xs">CONNECTED_12</span>
            </div>
            <div className="h-6 w-px bg-surface-line"></div>
            <div className="flex flex-col items-center">
               <span className="text-[9px] font-bold text-text-muted uppercase tracking-widest">Protocol</span>
               <span className="text-brand-primary font-mono text-xs">MQTTS_SECURE</span>
            </div>
          </div>
          <button className="bg-brand-primary text-black px-10 py-3.5 rounded font-bold text-xs shadow-[0_0_30px_rgba(0,255,102,0.3)] hover:scale-105 active:scale-95 transition-all uppercase tracking-[0.2em]">
            INITIALIZE_MAP_LAYER
          </button>
        </div>

        {/* HUD Decorations */}
        <div className="absolute top-6 left-6 border-l border-t border-brand-primary/30 w-12 h-12"></div>
        <div className="absolute top-6 right-6 border-r border-t border-brand-primary/30 w-12 h-12"></div>
        <div className="absolute bottom-6 left-6 border-l border-b border-brand-primary/30 w-12 h-12"></div>
        <div className="absolute bottom-6 right-6 border-r border-b border-brand-primary/30 w-12 h-12"></div>
      </div>
    </div>
  );
}

function AnalyticsView() {
  return (
    <div className="space-y-10">
      <div className="flex justify-between items-end">
        <div>
          <h2 className="text-3xl font-bold tracking-tight uppercase font-mono text-text-primary">Data_Intelligence</h2>
          <p className="text-text-muted font-medium text-sm tracking-wide">Performance reporting & volume analysis cluster</p>
        </div>
        <div className="flex gap-1 bg-surface-line p-1 rounded-sm">
          {['Week', 'Month', 'Quarter', 'Year'].map(t => (
            <button key={t} className={`px-5 py-2 rounded-sm text-[10px] font-bold uppercase tracking-widest transition-all ${t === 'Week' ? 'bg-brand-primary text-black shadow-lg' : 'text-text-muted hover:text-text-primary hover:bg-white/5'}`}>
              {t}
            </button>
          ))}
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-8">
        <div className="bg-surface-card p-10 rounded border border-surface-line shadow-sm space-y-8">
          <div className="flex justify-between items-center">
            <h3 className="font-bold text-base uppercase tracking-widest font-mono text-text-primary">Shipment_Volume_Telemetry</h3>
            <div className="flex gap-4">
              <div className="flex items-center gap-2 text-[9px] font-bold uppercase tracking-[0.2em] text-brand-primary">
                <div className="w-1.5 h-1.5 rounded-full bg-brand-primary shadow-[0_0_8px_rgba(0,255,102,0.5)]"></div>
                VOLUME_FLOW
              </div>
            </div>
          </div>
          <div className="h-72 mt-4">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={ANALYTICS_DATA}>
                <defs>
                  <linearGradient id="colorShip" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#00FF66" stopOpacity={0.2}/>
                    <stop offset="95%" stopColor="#00FF66" stopOpacity={0}/>
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#ffffff05" />
                <XAxis dataKey="name" stroke="#888888" fontSize={9} tickLine={false} axisLine={false} />
                <Tooltip 
                  contentStyle={{ 
                    backgroundColor: '#141414',
                    border: '1px solid #262626',
                    borderRadius: '4px',
                    fontSize: '11px',
                    fontWeight: 'bold',
                    color: '#00FF66'
                  }} 
                  itemStyle={{ color: '#00FF66' }}
                  cursor={{ stroke: '#00FF66', strokeWidth: 1 }}
                />
                <Area type="monotone" dataKey="shipments" stroke="#00FF66" fillOpacity={1} fill="url(#colorShip)" strokeWidth={2} />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </div>

        <div className="bg-surface-card p-10 rounded border border-surface-line shadow-sm space-y-8">
          <h3 className="font-bold text-base uppercase tracking-widest font-mono text-text-primary">Integrity_Stock_Fluctuations</h3>
          <div className="h-72 mt-4">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={ANALYTICS_DATA}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#ffffff05" />
                <XAxis dataKey="name" stroke="#888888" fontSize={9} tickLine={false} axisLine={false} />
                <Tooltip 
                   cursor={{fill: '#ffffff05'}}
                   contentStyle={{ 
                    backgroundColor: '#141414',
                    border: '1px solid #262626',
                    borderRadius: '4px',
                    fontSize: '11px',
                    fontWeight: 'bold'
                  }} 
                  itemStyle={{ color: '#00FF66' }}
                />
                <Bar dataKey="stock" fill="#00FF6620" radius={[2, 2, 0, 0]} />
                <Bar dataKey="shipments" fill="#00FF66" radius={[2, 2, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>
      </div>

      <div className="bg-surface-card text-text-primary p-12 rounded border border-surface-line relative overflow-hidden flex flex-col md:flex-row items-center justify-between gap-10">
        <div className="absolute top-0 right-0 w-[400px] h-[400px] bg-brand-primary/[0.03] technical-grid -rotate-12 translate-x-20 -translate-y-20 pointer-events-none"></div>
        <div className="absolute left-0 bottom-0 w-[400px] h-[400px] bg-brand-primary/[0.03] technical-grid rotate-45 translate-x-[-100px] translate-y-[100px] pointer-events-none text-brand-primary"></div>
        
        <div className="z-10 space-y-6">
          <div className="flex items-center gap-3">
             <div className="w-1.5 h-6 bg-brand-primary shadow-[0_0_10px_rgba(0,255,102,0.5)]"></div>
             <h3 className="text-2xl font-bold tracking-tighter uppercase font-mono">System_Audit_Ready</h3>
          </div>
          <p className="text-text-muted max-w-xl text-sm font-medium leading-relaxed">Your monthly logistics efficiency audit is ready. We've detected a <span className="text-brand-primary font-bold shadow-[0_0_5px_rgba(0,255,102,0.5)]">14% optimization potential</span> across the Northern distribution route segment.</p>
        </div>
        <button className="z-10 bg-brand-primary text-black px-10 py-4 rounded font-bold text-xs shadow-[0_0_20px_rgba(0,255,102,0.2)] hover:scale-105 active:scale-95 transition-all group shrink-0 uppercase tracking-widest">
          FETCH_EXECUTIVE_PDF
          <ArrowUpRight className="inline-block ml-3 group-hover:translate-x-1 group-hover:-translate-y-1 transition-transform" size={16} />
        </button>
      </div>
    </div>
  );
}

function DashboardView({ onCreateShipment }: { onCreateShipment: () => void }) {
  return (
    <div className="space-y-10">
      <div className="flex justify-between items-end">
        <div>
          <h2 className="text-3xl font-bold tracking-tight uppercase font-mono text-text-primary">Operational_Status</h2>
          <p className="text-text-muted font-medium text-sm tracking-wide">Real-time platform telemetry & kernel metrics</p>
        </div>
        <button 
          onClick={onCreateShipment}
          className="flex items-center gap-3 bg-brand-primary text-black px-6 py-3 rounded font-bold text-[11px] shadow-[0_0_20px_rgba(0,255,102,0.2)] hover:scale-105 active:scale-95 transition-all uppercase tracking-[0.2em]"
        >
          <Plus size={16} />
          New_Shipment_Thread
        </button>
      </div>

      {/* Stats Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
        <StatCard 
          title="Transit Assets" 
          value="482" 
          change="+12.0%" 
          isPositive={true} 
          icon={<Truck size={18} />} 
        />
        <StatCard 
          title="System Alerts" 
          value="04" 
          change="Critical" 
          isPositive={false} 
          icon={<AlertTriangle size={18} />} 
        />
        <StatCard 
          title="Warehouse Cap." 
          value="84.2%" 
          change="+2.1%" 
          isPositive={true} 
          icon={<Warehouse size={18} />} 
        />
        <StatCard 
          title="Driver Eff." 
          value="98.1%" 
          change="+0.8%" 
          isPositive={true} 
          icon={<User size={18} />} 
        />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
        {/* Recent Shipments Table */}
        <div className="lg:col-span-2 bg-surface-card rounded-none border border-surface-line overflow-hidden shadow-2xl flex flex-col">
          <div className="p-6 border-b border-surface-line flex justify-between items-center bg-white/[0.02]">
            <div className="flex items-center gap-3">
               <div className="w-1 h-4 bg-brand-primary shadow-[0_0_5px_rgba(0,255,102,0.5)]"></div>
               <h3 className="font-bold text-sm uppercase tracking-widest font-mono text-text-primary">Real-time_Transit_Status</h3>
            </div>
            <button className="text-[10px] font-bold text-text-muted hover:text-brand-primary flex items-center gap-1 transition-colors uppercase tracking-[0.2em]">
              Query_Logs <ChevronRight size={14} />
            </button>
          </div>
          <div className="overflow-x-auto flex-1">
            <table className="w-full text-left">
              <thead className="bg-surface-line text-[10px] uppercase font-bold tracking-[0.2em] text-text-muted bg-white/[0.03]">
                <tr>
                  <th className="px-6 py-4 border-b border-surface-line">PID</th>
                  <th className="px-6 py-4 border-b border-surface-line">HUB_TRAJECTORY</th>
                  <th className="px-6 py-4 border-b border-surface-line">ASSET_OPERATOR</th>
                  <th className="px-6 py-4 border-b border-surface-line">PHASE</th>
                  <th className="px-6 py-4 border-b border-surface-line">EST_TIME</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-surface-line">
                {SHIPMENTS.map((shp) => (
                  <tr key={shp.id} className="hover:bg-white/[0.02] transition-colors cursor-pointer group">
                    <td className="px-6 py-4 font-mono text-xs font-bold text-brand-primary">{shp.id}</td>
                    <td className="px-6 py-4 text-xs font-bold tracking-tight text-text-primary uppercase group-hover:translate-x-1 transition-transform">{shp.destination}</td>
                    <td className="px-6 py-4">
                      <div className="flex flex-col">
                        <span className="text-[11px] font-bold uppercase text-text-primary">{shp.vehicle}</span>
                        <span className="text-[9px] font-bold text-text-muted uppercase tracking-widest mt-0.5">{shp.driver}</span>
                      </div>
                    </td>
                    <td className="px-6 py-4">
                      <StatusBadge status={shp.status} />
                    </td>
                    <td className="px-6 py-4 text-[11px] font-mono font-bold text-text-muted group-hover:text-brand-primary transition-colors">{shp.eta}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        {/* System Health / Alerts */}
        <div className="bg-surface-card rounded-none border border-surface-line p-8 shadow-2xl flex flex-col h-full space-y-8">
           <div className="flex items-center gap-3">
               <div className="w-1 h-4 bg-status-error shadow-[0_0_10px_rgba(255,51,51,0.5)]"></div>
               <h3 className="font-bold text-sm uppercase tracking-widest font-mono text-text-primary">Critical_Segments</h3>
            </div>
          <div className="space-y-6 flex-1">
            {INVENTORY_DATA.filter(i => i.status !== 'optimal').map(item => (
              <div key={item.id} className="flex gap-5 p-5 bg-white/[0.02] rounded border border-surface-line hover:border-brand-primary/30 transition-all cursor-pointer group">
                <div className={`shrink-0 w-12 h-12 rounded bg-surface-line flex items-center justify-center border border-white/[0.05] ${item.status === 'out' ? 'text-status-error group-hover:shadow-[0_0_15px_rgba(255,51,51,0.2)]' : 'text-status-warning group-hover:shadow-[0_0_15px_rgba(245,158,11,0.2)]'}`}>
                  <AlertTriangle size={24} />
                </div>
                <div className="flex-1 overflow-hidden space-y-1">
                  <div className="flex justify-between items-start">
                    <p className="font-bold text-sm truncate uppercase tracking-tight group-hover:text-brand-primary transition-colors text-text-primary">{item.name}</p>
                    <div className="scale-75 origin-right">
                      <StatusBadge status={item.status} />
                    </div>
                  </div>
                  <p className="text-[9px] text-text-muted font-bold uppercase tracking-widest">{item.id} // {item.location}</p>
                  <div className="pt-2 flex items-center gap-2">
                     <span className="text-[10px] font-bold text-text-muted uppercase">LOAD:</span>
                     <div className="flex-1 h-1 bg-surface-line rounded-full overflow-hidden">
                        <div 
                          className={`h-full ${item.status === 'out' ? 'bg-status-error' : 'bg-status-warning'}`}
                          style={{ width: `${item.stock}%` }}
                        ></div>
                     </div>
                     <span className="text-xs font-mono font-bold text-text-primary">{item.stock}%</span>
                  </div>
                </div>
              </div>
            ))}
          </div>
          
          <div className="pt-6 border-t border-surface-line space-y-4">
             <div className="flex justify-between items-center text-[10px] font-bold tracking-[0.2em] text-text-muted uppercase">
                <span>CORE_UTILIZATION</span>
                <span className="text-brand-primary font-mono">65%_LOAD</span>
             </div>
             <div className="h-1 bg-surface-line w-full">
                <div className="h-full bg-brand-primary shadow-[0_0_10px_rgba(0,255,102,0.5)]" style={{ width: '65%' }}></div>
             </div>
             <div className="flex justify-between text-[9px] font-mono text-text-muted uppercase">
                <span>TEMP: 34°C</span>
                <span>VOLT: 1.2V</span>
             </div>
          </div>
          
          <button className="w-full py-4 border border-brand-primary/20 bg-brand-primary/5 text-brand-primary rounded font-bold text-[10px] hover:bg-brand-primary hover:text-black transition-all uppercase tracking-[0.3em] shadow-inner">
            Execute_Restock_Protocol
          </button>
        </div>
      </div>
    </div>
  );
}

function WarehouseView() {
  return (
    <div className="space-y-10">
       <div className="flex justify-between items-end">
        <div>
          <h2 className="text-3xl font-bold tracking-tight uppercase font-mono text-text-primary">Warehouse_Cluster</h2>
          <p className="text-text-muted font-medium text-sm tracking-wide">Inventory tracking & location management database</p>
        </div>
        <div className="flex gap-4">
          <button className="bg-transparent border border-surface-line text-text-muted px-6 py-3 rounded font-bold text-[11px] hover:text-text-primary hover:bg-surface-line transition-all uppercase tracking-widest">
            Export_Records
          </button>
          <button className="bg-brand-primary text-black px-6 py-3 rounded font-bold text-[11px] shadow-[0_0_20px_rgba(0,255,102,0.2)] hover:scale-105 active:scale-95 transition-all uppercase tracking-widest">
            Insert_Product_Object
          </button>
        </div>
      </div>

      <div className="bg-surface-card rounded-none border border-surface-line overflow-hidden shadow-2xl flex flex-col focus-within:border-brand-primary/50 transition-all">
        <div className="p-8 border-b border-surface-line bg-white/[0.01]">
          <div className="flex flex-col md:flex-row gap-6 items-center">
            <div className="relative flex-1 w-full group">
              <Search className="absolute left-4 top-1/2 -translate-y-1/2 text-text-muted group-focus-within:text-brand-primary transition-colors" size={16} />
              <input 
                type="text" 
                placeholder="search_sku_query..." 
                className="w-full pl-12 pr-4 py-3 bg-surface-line/50 border border-transparent border-b-surface-line focus:border-brand-primary rounded-t text-xs font-mono font-bold text-text-primary focus:outline-none transition-all placeholder:text-text-muted/30"
              />
            </div>
            <div className="flex gap-2 w-full md:w-auto">
              {['GLOBAL_DASHBOARD', 'CATEGORIES', 'SECURITY_LEVEL'].map(filter => (
                <button key={filter} className="px-5 py-3 rounded-sm border border-surface-line text-[9px] font-bold text-text-muted bg-surface-line/30 hover:border-brand-primary hover:text-brand-primary transition-all shrink-0 uppercase tracking-widest">
                  {filter}
                </button>
              ))}
            </div>
          </div>
        </div>
        <div className="overflow-x-auto flex-1">
          <table className="w-full text-left">
            <thead className="bg-surface-line text-[10px] uppercase font-bold tracking-[0.2em] text-text-muted border-b border-surface-line">
              <tr>
                <th className="px-8 py-5">SKU_ID</th>
                <th className="px-8 py-5">OBJECT_NAME</th>
                <th className="px-8 py-5">METADATA_TAG</th>
                <th className="px-8 py-5 text-center">LOAD_PERCENTAGE</th>
                <th className="px-8 py-5">LOC_COORD</th>
                <th className="px-8 py-5">STATUS_FLAG</th>
                <th className="px-8 py-5"></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-surface-line">
              {INVENTORY_DATA.map((item) => (
                <tr key={item.id} className="hover:bg-white/[0.02] transition-all group cursor-pointer">
                  <td className="px-8 py-6 font-mono text-xs font-bold text-brand-primary group-hover:scale-105 transition-transform">{item.id}</td>
                  <td className="px-8 py-6">
                    <div className="font-bold text-sm tracking-tight uppercase group-hover:text-brand-primary transition-colors text-text-primary">{item.name}</div>
                  </td>
                  <td className="px-8 py-6 uppercase">
                    <span className="text-[10px] font-bold text-text-muted bg-white/[0.05] px-2.5 py-1 rounded border border-white/[0.05] tracking-widest">{item.category}</span>
                  </td>
                  <td className="px-8 py-6">
                    <div className="flex flex-col items-center gap-2 min-w-[140px]">
                      <div className="w-full h-1 bg-white/5 rounded-none overflow-hidden">
                        <div 
                          className={`h-full transition-all duration-1000 ${
                            item.status === 'optimal' ? 'bg-brand-primary shadow-[0_0_8px_rgba(0,255,102,0.5)]' : 
                            item.status === 'low' ? 'bg-status-warning shadow-[0_0_8px_rgba(245,158,11,0.5)]' : 'bg-status-error shadow-[0_0_8px_rgba(255,51,51,0.5)]'
                          }`}
                          style={{ width: `${Math.min((item.stock / 100) * 100, 100)}%` }}
                        />
                      </div>
                      <span className="text-[10px] font-mono font-bold tracking-tighter opacity-80 group-hover:opacity-100 transition-opacity text-text-primary">
                        LOAD:{item.stock}% <span className="text-text-muted font-sans font-normal opacity-50"> / 100 </span>
                      </span>
                    </div>
                  </td>
                  <td className="px-8 py-6 uppercase">
                    <div className="flex items-center gap-3">
                       <div className="w-1.5 h-1.5 rounded-full bg-brand-primary/40 group-hover:bg-brand-primary shadow-[0_0_5px_rgba(0,255,102,0.5)] transition-all" />
                       <span className="text-xs font-bold font-mono text-text-muted group-hover:text-text-primary transition-colors uppercase tracking-tight">{item.location}</span>
                    </div>
                  </td>
                  <td className="px-8 py-6">
                    <StatusBadge status={item.status} />
                  </td>
                  <td className="px-8 py-6 text-right">
                    <button className="p-2.5 hover:bg-white/5 rounded text-text-muted group-hover:text-brand-primary transition-all transform group-hover:translate-x-1">
                      <ChevronRight size={20} />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
