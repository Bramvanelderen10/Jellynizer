import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences_platform_interface/in_memory_shared_preferences_async.dart';
import 'package:shared_preferences_platform_interface/shared_preferences_async_platform_interface.dart';

import 'package:jellynizer_app/services/storage_service.dart';

void main() {
  setUp(() {
    // StorageService uses SharedPreferencesAsync, which needs a platform implementation.
    SharedPreferencesAsyncPlatform.instance =
        InMemorySharedPreferencesAsync.empty();
  });

  test('returns null when nothing is stored', () async {
    expect(await StorageService().getApiUrl(), isNull);
  });

  test('stores and returns the API URL', () async {
    final storage = StorageService();

    await storage.setApiUrl('192.168.1.10:45263');

    expect(await storage.getApiUrl(), '192.168.1.10:45263');
  });

  test('treats an empty stored value as not configured', () async {
    final storage = StorageService();

    await storage.setApiUrl('');

    expect(await storage.getApiUrl(), isNull);
  });

  test('clears the stored API URL', () async {
    final storage = StorageService();
    await storage.setApiUrl('192.168.1.10:45263');

    await storage.clearApiUrl();

    expect(await storage.getApiUrl(), isNull);
  });
}
