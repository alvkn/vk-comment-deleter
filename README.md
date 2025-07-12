# VK Comment Deleter

Утилита для массового удаления комментариев ВКонтакте из архива данных.

## Описание

Программа анализирует HTML файлы из архива данных ВКонтакте, извлекает ссылки на комментарии и удаляет их через VK API. Использует батчевое удаление для высокой производительности (до 25 комментариев за запрос).

## Использование

### Готовые сборки

Скачайте последнюю версию из [Releases](../../releases):

- **Windows**: `vk-comments-deleter-win-x64.zip`
- **Linux**: `vk-comments-deleter-linux-x64.tar.gz`

### Сборка из исходного кода

```bash
# Клонирование репозитория
git clone https://github.com/yourusername/vk-comment-deleter.git
cd vk-comment-deleter

# Восстановление зависимостей
dotnet restore

# Сборка
dotnet build -c Release

# Запуск
dotnet run
```

## Требования

- **.NET 9.0** (включен в self-contained сборки)
- **Access Token ВКонтакте** с правами на управление комментариями
- **Архив данных ВК** с папкой `comments`

## Использование

### 1. Получение Access Token

1. Перейдите на [vkhost](https://vkhost.github.io/)
2. Получите токен от Kate Mobile

### 2. Подготовка данных

1. Скачайте архив данных в [разделе защиты данных ВК](https://vk.com/data_protection?section=rules)
2. Распакуйте архив
3. Убедитесь, что папка `comments` содержит HTML файлы

### 3. Запуск программы

```bash
# Windows
./VkCommentsDeleter.exe

# Linux
./VkCommentsDeleter
```

### 4. Следуйте инструкциям

1. Введите Access Token
2. Укажите путь к распакованному архиву
3. Подтвердите операцию удаления
4. Дождитесь завершения обработки
