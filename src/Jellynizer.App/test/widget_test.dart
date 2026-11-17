import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences_platform_interface/in_memory_shared_preferences_async.dart';
import 'package:shared_preferences_platform_interface/shared_preferences_async_platform_interface.dart';

import 'package:jellynizer_app/di/service_locator.dart';
import 'package:jellynizer_app/screens/setup/setup_screen.dart';
import 'package:jellynizer_app/services/storage_service.dart';

void main() {
  setUp(() async {
    // StorageService constructs a SharedPreferencesAsync, which throws unless a platform
    // implementation is registered. Use the in-memory one so this runs without a device.
    SharedPreferencesAsyncPlatform.instance =
        InMemorySharedPreferencesAsync.empty();
    await getIt.reset();
  });

  tearDown(() => getIt.reset());

  testWidgets('Setup screen renders', (WidgetTester tester) async {
    setupServiceLocator();

    await tester.pumpWidget(
      MaterialApp(home: SetupScreen(storage: getIt<StorageService>())),
    );

    expect(find.text('Jellynizer'), findsOneWidget);
    expect(
      find.text('Enter the address of your Jellynizer API to get started.'),
      findsOneWidget,
    );
  });
}
