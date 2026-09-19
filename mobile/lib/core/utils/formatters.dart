import 'package:intl/intl.dart';

class Formatters {
  const Formatters._();

  static final _currency = NumberFormat.currency(locale: 'en_KE', symbol: 'KES ', decimalDigits: 2);
  static final _date = DateFormat('d MMM yyyy');
  static final _dateTime = DateFormat('d MMM yyyy, h:mm a');

  static String money(double amount) => _currency.format(amount);
  static String date(DateTime value) => _date.format(value);
  static String dateTime(DateTime value) => _dateTime.format(value.toLocal());
}
