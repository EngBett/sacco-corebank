import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/di/service_locator.dart';
import '../../domain/entities/dividend.dart';
import '../../domain/repositories/dividends_repository.dart';

final dividendsRepositoryProvider = Provider<DividendsRepository>((ref) => sl<DividendsRepository>());

final myDividendsProvider = FutureProvider.autoDispose<List<MyDividend>>(
  (ref) => ref.watch(dividendsRepositoryProvider).getMyDividends(),
);
