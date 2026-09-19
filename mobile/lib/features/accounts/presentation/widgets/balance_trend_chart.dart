import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../../../../core/theme/app_theme.dart';
import '../../../../core/utils/formatters.dart';
import '../../domain/entities/account_statement.dart';
import '../../domain/entities/balance_trend.dart';

/// The wallet reference's line chart — dashed grid, curved gradient line fading into the canvas, month labels — drawn
/// from an account's real month-end balances. The line rises from the baseline once on first show (skipped under
/// reduced motion); touching it shows the exact month-end balance.
class BalanceTrendChart extends StatefulWidget {
  const BalanceTrendChart({super.key, required this.statement, this.label});

  final AccountStatement statement;

  /// Read out by screen readers ahead of the numbers, e.g. "FOSA (current)".
  final String? label;

  @override
  State<BalanceTrendChart> createState() => _BalanceTrendChartState();
}

class _BalanceTrendChartState extends State<BalanceTrendChart> {
  bool _revealed = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) setState(() => _revealed = true);
    });
  }

  @override
  Widget build(BuildContext context) {
    final accent = Theme.of(context).colorScheme.primary;
    final trend = BalanceTrend.fromStatement(widget.statement, DateTime.now());
    final reduced = MediaQuery.disableAnimationsOf(context);
    final show = _revealed || reduced;

    final lo = trend.min;
    final hi = trend.max;
    final pad = trend.isFlat ? (hi.abs() * 0.1 + 1) : (hi - lo) * 0.2;
    final minY = lo - pad;
    final maxY = hi + pad;
    final spots = [
      for (var i = 0; i < trend.points.length; i++) FlSpot(i.toDouble(), show ? trend.points[i].balance : minY),
    ];
    final month = DateFormat('MMM');

    return Semantics(
      label:
          '${widget.label ?? 'Balance'} over the last ${trend.points.length} months: from '
          '${Formatters.money(trend.points.first.balance)} to ${Formatters.money(trend.points.last.balance)}',
      excludeSemantics: true,
      child: LineChart(
        duration: reduced ? Duration.zero : const Duration(milliseconds: 600),
        curve: Curves.easeOutCubic,
        LineChartData(
          minX: 0,
          maxX: (trend.points.length - 1).toDouble(),
          minY: minY,
          maxY: maxY,
          borderData: FlBorderData(show: false),
          gridData: FlGridData(
            drawVerticalLine: false,
            horizontalInterval: (maxY - minY) / 3,
            getDrawingHorizontalLine: (_) => const FlLine(color: AppTheme.gridLine, strokeWidth: 1, dashArray: [3, 4]),
          ),
          titlesData: FlTitlesData(
            leftTitles: const AxisTitles(),
            rightTitles: const AxisTitles(),
            topTitles: const AxisTitles(),
            bottomTitles: AxisTitles(
              sideTitles: SideTitles(
                showTitles: true,
                interval: 1,
                reservedSize: 26,
                getTitlesWidget: (value, meta) {
                  final i = value.round();
                  if (i != value || i < 0 || i >= trend.points.length) return const SizedBox.shrink();
                  return SideTitleWidget(
                    meta: meta,
                    fitInside: SideTitleFitInsideData.fromTitleMeta(meta, distanceFromEdge: 0),
                    child: Text(
                      month.format(trend.points[i].month).toUpperCase(),
                      style: const TextStyle(color: AppTheme.textSecondary, fontSize: 11, fontWeight: FontWeight.w700),
                    ),
                  );
                },
              ),
            ),
          ),
          lineTouchData: LineTouchData(
            getTouchedSpotIndicator: (bar, indexes) => [
              for (final _ in indexes)
                TouchedSpotIndicatorData(
                  const FlLine(color: AppTheme.border, strokeWidth: 1, dashArray: [4, 3]),
                  FlDotData(
                    getDotPainter: (spot, percent, bar, index) =>
                        FlDotCirclePainter(radius: 5, color: accent, strokeWidth: 2, strokeColor: AppTheme.surface),
                  ),
                ),
            ],
            touchTooltipData: LineTouchTooltipData(
              getTooltipColor: (_) => AppTheme.surfaceRaised,
              tooltipBorderRadius: BorderRadius.circular(10),
              getTooltipItems: (spots) => [
                for (final s in spots)
                  LineTooltipItem(
                    '${month.format(trend.points[s.x.round()].month)}\n',
                    const TextStyle(color: AppTheme.textSecondary, fontSize: 11),
                    children: [
                      TextSpan(
                        text: Formatters.money(trend.points[s.x.round()].balance),
                        style: AppTheme.money(size: 13),
                      ),
                    ],
                  ),
              ],
            ),
          ),
          lineBarsData: [
            LineChartBarData(
              spots: spots,
              isCurved: true,
              curveSmoothness: 0.3,
              preventCurveOverShooting: true,
              barWidth: 2.5,
              isStrokeCapRound: true,
              gradient: LinearGradient(colors: [AppTheme.chartCyan, accent]),
              dotData: FlDotData(
                checkToShowDot: (spot, bar) => spot.x == bar.spots.last.x,
                getDotPainter: (spot, percent, bar, index) =>
                    FlDotCirclePainter(radius: 4, color: accent, strokeWidth: 2, strokeColor: AppTheme.surface),
              ),
              belowBarData: BarAreaData(
                show: true,
                gradient: LinearGradient(
                  begin: Alignment.topCenter,
                  end: Alignment.bottomCenter,
                  colors: [accent.withValues(alpha: 0.22), accent.withValues(alpha: 0)],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
