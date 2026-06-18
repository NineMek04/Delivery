import 'package:flutter/material.dart';

class NavigationInstructionCard extends StatelessWidget {
  const NavigationInstructionCard({
    super.key,
    required this.icon,
    required this.title,
    required this.subtitle,
    this.onOpenExternalMap,
    this.isFallbackRoute = false,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final VoidCallback? onOpenExternalMap;
  final bool isFallbackRoute;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final warningColor = Colors.amber.shade700;

    return SafeArea(
      bottom: false,
      child: Material(
        elevation: 10,
        color: Colors.transparent,
        borderRadius: BorderRadius.circular(22),
        child: Container(
          padding: const EdgeInsets.fromLTRB(18, 14, 14, 14),
          decoration: BoxDecoration(
            color: const Color(0xFF0F4F4D).withOpacity(0.96),
            borderRadius: BorderRadius.circular(22),
            boxShadow: [
              BoxShadow(
                color: Colors.black.withOpacity(0.18),
                blurRadius: 18,
                offset: const Offset(0, 8),
              ),
            ],
          ),
          child: Row(
            children: [
              Icon(icon, color: Colors.white, size: 42),
              const SizedBox(width: 16),
              Expanded(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      title,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: theme.textTheme.titleLarge?.copyWith(
                        color: Colors.white,
                        fontWeight: FontWeight.w800,
                        height: 1.1,
                      ),
                    ),
                    const SizedBox(height: 6),
                    Row(
                      children: [
                        if (isFallbackRoute) ...[
                          Icon(
                            Icons.warning_amber_rounded,
                            color: warningColor,
                            size: 16,
                          ),
                          const SizedBox(width: 4),
                        ],
                        Expanded(
                          child: Text(
                            subtitle,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: theme.textTheme.bodyMedium?.copyWith(
                              color: Colors.white.withOpacity(0.78),
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
              const SizedBox(width: 10),
              _RoundControlButton(
                icon: Icons.assistant_navigation,
                tooltip: 'Open navigation',
                onPressed: onOpenExternalMap,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class NavigationBottomEtaPanel extends StatelessWidget {
  const NavigationBottomEtaPanel({
    super.key,
    required this.title,
    required this.etaText,
    required this.distanceText,
    this.statusText,
    this.actionLabel,
    this.onClose,
    this.onOverview,
    this.onAction,
    this.actionBusy = false,
  });

  final String title;
  final String etaText;
  final String distanceText;
  final String? statusText;
  final String? actionLabel;
  final VoidCallback? onClose;
  final VoidCallback? onOverview;
  final VoidCallback? onAction;
  final bool actionBusy;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return SafeArea(
      top: false,
      child: Material(
        color: Colors.transparent,
        elevation: 14,
        borderRadius: const BorderRadius.vertical(top: Radius.circular(28)),
        child: Container(
          padding: const EdgeInsets.fromLTRB(20, 10, 20, 18),
          decoration: const BoxDecoration(
            color: Colors.white,
            borderRadius: BorderRadius.vertical(top: Radius.circular(28)),
            boxShadow: [
              BoxShadow(
                color: Color(0x22000000),
                blurRadius: 18,
                offset: Offset(0, -8),
              ),
            ],
          ),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(
                width: 42,
                height: 4,
                decoration: BoxDecoration(
                  color: Colors.black12,
                  borderRadius: BorderRadius.circular(99),
                ),
              ),
              const SizedBox(height: 12),
              Row(
                children: [
                  _RoundControlButton(
                    icon: Icons.close,
                    tooltip: 'Close navigation',
                    onPressed: onClose,
                  ),
                  Expanded(
                    child: Column(
                      children: [
                        Text(
                          etaText,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: theme.textTheme.headlineSmall?.copyWith(
                            color: const Color(0xFF138A36),
                            fontWeight: FontWeight.w900,
                          ),
                        ),
                        const SizedBox(height: 2),
                        Text(
                          '$distanceText${statusText == null ? '' : ' - $statusText'}',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: theme.textTheme.bodyMedium?.copyWith(
                            color: Colors.black54,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                        const SizedBox(height: 3),
                        Text(
                          title,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: theme.textTheme.bodySmall?.copyWith(
                            color: Colors.black45,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ],
                    ),
                  ),
                  _RoundControlButton(
                    icon: Icons.alt_route,
                    tooltip: 'Route overview',
                    onPressed: onOverview,
                  ),
                ],
              ),
              if (actionLabel != null && onAction != null) ...[
                const SizedBox(height: 14),
                SizedBox(
                  width: double.infinity,
                  child: ElevatedButton(
                    onPressed: actionBusy ? null : onAction,
                    style: ElevatedButton.styleFrom(
                      backgroundColor: const Color(0xFF0F766E),
                      foregroundColor: Colors.white,
                      padding: const EdgeInsets.symmetric(vertical: 14),
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(16),
                      ),
                    ),
                    child: actionBusy
                        ? const SizedBox(
                            width: 20,
                            height: 20,
                            child: CircularProgressIndicator(
                              strokeWidth: 2,
                              color: Colors.white,
                            ),
                          )
                        : Text(
                            actionLabel!,
                            style: const TextStyle(
                              fontWeight: FontWeight.w800,
                            ),
                          ),
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class NavigationFloatingControls extends StatelessWidget {
  const NavigationFloatingControls({
    super.key,
    this.onCenter,
    this.onOverview,
    this.onReport,
    this.onToggleSound,
    this.soundEnabled = true,
  });

  final VoidCallback? onCenter;
  final VoidCallback? onOverview;
  final VoidCallback? onReport;
  final VoidCallback? onToggleSound;
  final bool soundEnabled;

  @override
  Widget build(BuildContext context) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        _RoundControlButton(
          icon: Icons.explore,
          tooltip: 'Center map',
          onPressed: onCenter,
        ),
        const SizedBox(height: 12),
        _RoundControlButton(
          icon: Icons.search,
          tooltip: 'Route overview',
          onPressed: onOverview,
        ),
        const SizedBox(height: 12),
        _RoundControlButton(
          icon: soundEnabled ? Icons.volume_up : Icons.volume_off,
          tooltip: 'Toggle sound',
          onPressed: onToggleSound,
        ),
        const SizedBox(height: 12),
        _PillControlButton(
          icon: Icons.report_problem_outlined,
          label: 'Report',
          onPressed: onReport,
        ),
      ],
    );
  }
}

class _RoundControlButton extends StatelessWidget {
  const _RoundControlButton({
    required this.icon,
    required this.tooltip,
    this.onPressed,
  });

  final IconData icon;
  final String tooltip;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: Colors.white,
      shape: const CircleBorder(),
      elevation: 5,
      child: IconButton(
        tooltip: tooltip,
        icon: Icon(icon, color: Colors.black87),
        onPressed: onPressed,
      ),
    );
  }
}

class _PillControlButton extends StatelessWidget {
  const _PillControlButton({
    required this.icon,
    required this.label,
    this.onPressed,
  });

  final IconData icon;
  final String label;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: Colors.white,
      borderRadius: BorderRadius.circular(999),
      elevation: 5,
      child: InkWell(
        borderRadius: BorderRadius.circular(999),
        onTap: onPressed,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(icon, color: Colors.amber.shade800, size: 22),
              const SizedBox(width: 8),
              Text(
                label,
                style: const TextStyle(
                  color: Colors.black87,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
