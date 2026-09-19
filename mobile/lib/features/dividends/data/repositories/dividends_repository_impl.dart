import '../../domain/entities/dividend.dart';
import '../../domain/repositories/dividends_repository.dart';
import '../datasources/dividends_remote_datasource.dart';

class DividendsRepositoryImpl implements DividendsRepository {
  DividendsRepositoryImpl(this._remote);
  final DividendsRemoteDataSource _remote;

  @override
  Future<List<MyDividend>> getMyDividends({int? year}) async =>
      (await _remote.getMyDividends(year: year)).map(MyDividend.fromJson).toList();
}
