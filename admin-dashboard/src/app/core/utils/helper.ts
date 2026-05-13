export function pathCombine(path1: string, path2: string): string {
    if (!path1) return path2;
    if (!path2) return path1;
    
    const p1 = path1.endsWith('/') ? path1.substring(0, path1.length - 1) : path1;
    const p2 = path2.startsWith('/') ? path2.substring(1) : path2;
    
    return `${p1}/${p2}`;
}
