# واجهة برمجية Flow Image المحلية والـ SDK البرمجي (الموحد)

[![Python Version](https://img.shields.io/badge/python-3.10%20%7C%203.11%20%7C%203.12-blue)](https://www.python.org/)
[![License](https://img.shields.io/badge/license-MIT-green)](./LICENSE)

النسخة الإنجليزية: [README.md](./README.md) | تفاصيل المعمارية: [ARCHITECTURE-ar.md](./ARCHITECTURE-ar.md)

يوفر هذا المستودع بيئة تشغيل محلية موحدة لتوليد الصور عبر Google Flow، وتجمع عدة أدوات برمجية أساسية في حزمة موحدة. يمكن استخدام هذا المشروع كمكتبة استدعاء برمجية عالية المستوى لـ Python (FlowSDK) أو تشغيله كخادم واجهة برمجية محلي متوافق مع معايير OpenAI.

---

## 🗺️ البنية البرمجية والتقسيم الهيكلي

ينقسم المشروع إلى **4 وحدات برمجية أساسية** تدير جوانب الخدمة المختلفة:

### 📦 الجزء 1: مكتبة الاستدعاء البرمجية (FlowSDK)
تسمح لك فئة الـ SDK البرمجية بدمج توليد الصور مباشرة في تطبيقات بايثون الخاصة بك دون الحاجة لخوادم وسيطة أو جسور كابتشا خارجية. وتتميز بـ:
* **عزل التكوينات (Thread-Safe Context Isolation)**: باستخدام سياق `async with FlowSDK(...)` لمنع تداخل الإعدادات.
* **تحديد الجلسات والحسابات**: دعم التمرير المباشر لرموز الاتصال (Direct Tokens) أو الاختيار التلقائي للحسابات من قاعدة البيانات.
* **مثال للاستخدام**:
```python
import asyncio
from google_flow import FlowSDK

async def main():
    # الخيار 1: تجاوز الجلسة يدوياً بتوكن اتصال مباشر
    async with FlowSDK(st_token="your-session-token", project_id="your-project-id") as sdk:
        image_path = await sdk.generate(
            prompt="A futuristic city in antigravity, neon lights, 4k",
            model="gemini-3.1-flash-image-landscape",
            output_path="output/direct_t2i.png"
        )
        print(f"تم حفظ الصورة في: {image_path}")

    # الخيار 2: الاختيار التلقائي للحساب (يستخرج البيانات تلقائياً من قاعدة بيانات SQLite)
    async with FlowSDK() as sdk:
        await sdk.select_profile("My_Google_Profile_Name")
        image_path = await sdk.generate(
            prompt="A majestic golden eagle flying over mountains",
            model="gemini-3.1-flash-image-square",
            output_path="output/profile_t2i.png"
        )
        print(f"تم حفظ الصورة في: {image_path}")

if __name__ == "__main__":
    asyncio.run(main())
```

---

### 🌐 الجزء 2: خادم الواجهة البرمجية (API) المتوافق مع OpenAI
يوفر واجهة برمجية متوافقة بالكامل مع بروتوكول OpenAI لربط مولد الصور بتطبيقاتك المفضلة.
* **التوافق التام**: يعمل مباشرة مع برامج مثل Cherry Studio أو Next Chat عبر الرابط الأساسي ومفتاح الترخيص.
* **لوحة التحكم الرسومية وإعداد معالج الحسابات (`/setup`)**: واجهة ويب ذكية تفتح تلقائياً لتسهيل تسجيل الدخول والتقاط التوكنات وتدبير الإعدادات يدوياً.
* **البداية السريعة**:
  1. انقر نقراً مزدوجاً على `install.bat` لتهيئة البيئة الافتراضية.
  2. انقر نقراً مزدوجاً على `start-flow-api.bat` لتشغيل خادم الويب.
  3. قم بإنهاء إعداد الحساب عبر الرابط `http://127.0.0.1:8787/setup`.
* **نقاط النهاية (Endpoints)**:
  * `POST /v1/images/generations` - توليد الصور من النصوص
  * `POST /v1/images/edits` - تعديل وتوليد الصور من صور
  * `GET /v1/models` - سرد النماذج المتاحة
  * `GET /setup` - معالج الإعداد التلقائي

---

### 🔄 الجزء 3: خدمة تحديث الرموز والتوكنات التلقائية (Token Updater)
خدمة جدولة خلفية تعمل على فحص وضمان استمرار صلاحية رموز الجلسات للحسابات بشكل دائم.
* **جدولة دورية نشطة**: تستخدم مجدول المهام `APScheduler` للتحقق من صلاحية الحسابات بشكل دوري.
* **طرق التحديث**: دعم التجديد عبر البروتوكول (Protocol-based) الخفيف والصامت، أو التحديث التفاعلي بفتح متصفح للمستخدم في حال تطلب جوجل التحقق البشري.
* **عزل الشبكة (Proxy Isolation)**: إمكانية تعيين عنوان بروكسي فريد لكل حساب لمنع كشف البصمة الرقمية للحسابات المشتركة.

---

### 🔑 الجزء 4: خدمة تخطي وحل الكابتشا داخلياً (Captcha Solver)
تقوم بحل وتجاوز حماية الكابتشا (reCAPTCHA) الخاصة بـ Google Flow برمجياً داخل نفس خيط عمل التطبيق.
* **حل داخلي بالكامل**: يتكامل مباشرة مع مشغل المتصفح عبر Playwright و nodriver دون الحاجة لتشغيل جسر خادم خارجي مستقل.
* **تحسين استهلاك الموارد**: إعادة استخدام التبويبات المفتوحة (Resident Tabs) مما يقلل وقت البداية الباردة واستهلاك الموارد بنسبة تصل لـ 60%.

---

## 📁 هيكلية المستودع وتوزيع الملفات

```text
flow-image-cli-local-api/
├── google_flow/              # الحزمة الأساسية الموحدة للمشروع
│   ├── api/                  # الجزء 2: خادم ويب FastAPI ولوحة التحكم الرسومية
│   ├── captcha/              # الجزء 4: خطافات ومزودي حل الكابتشا داخل العملية
│   ├── captcha_service/      # الجزء 4: محرك حل الكابتشا (nodriver/playwright)
│   ├── token_updater/        # الجزء 3: خدمة تحديث ومراقبة الجلسات التلقائية
│   ├── core/                 # الجزء 1: فئة الـ SDK والعميل البرمجي (FlowSDK)
│   └── utils/                # أدوات التحليل المشتركة والبروكسي وقواعد البيانات
├── examples/                 # أمثلة استدعاء برمجية جاهزة للتشغيل
├── release-package/          # أدوات بناء حزم التوزيع النظيفة للمستخدم النهائي
├── install.bat               # سكريبت التثبيت بنقرة واحدة لويندوز
├── start-flow-api.bat        # سكريبت بدء التشغيل بنقرة واحدة لويندوز
├── API_USAGE.md              # أمثلة cURL وطلبات الـ API المتوافقة
└── README-ar.md              # هذا الملف التوضيحي باللغة العربية
```

---

## 📚 مراجع ومشاريع معمارية أساسية (References)

تعتبر هذه النسخة الموحدة مبنية ومطورة بالاستناد على المشاريع والمستودعات المرجعية التالية:
* **خدمة ومحرك حل الكابتشا**: [flow_captcha_service](https://github.com/genz27/flow_captcha_service)
* **خدمة ومجدول تحديث التوكنات**: [flow2api_tupdater](https://github.com/genz27/flow2api_tupdater)
* **قاعدة الأكواد المحلية وخادم الويب الأساسي**: [flow-image-cli-local-api](https://github.com/cdm16888/flow-image-cli-local-api)

---

## 📝 الترخيص

هذا المشروع مرخص بموجب ترخيص MIT.
