import '../entities/dividend.dart';

abstract class DividendsRepository {
  /// Every year the member has a line for, most recent first. Pass [year] to
  /// scope to a single financial year (`GET /api/self/dividends?year=`).
  Future<List<MyDividend>> getMyDividends({int? year});
}
