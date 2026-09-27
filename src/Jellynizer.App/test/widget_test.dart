import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:jellynizer_app/di/service_locator.dart';
import 'package:jellynizer_app/screens/setup/setup_screen.dart';
import 'package:jellynizer_app/services/storage_service.dart';

void main() {
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
