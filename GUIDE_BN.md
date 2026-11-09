# School Admission System — বাংলা সেটআপ ও সাবমিশন গাইড

এই গাইডটি Windows PowerShell ও Docker Desktop ব্যবহার করে Project চালানো, পরীক্ষা করা এবং GitHub-এ জমা দেওয়ার জন্য।

## ১. প্রয়োজনীয় বিষয়

- Docker Desktop ইনস্টল এবং Running থাকতে হবে। Windows-এ WSL 2/Virtual Machine Platform সক্রিয় থাকতে হবে।
- GitHub-এ Public Repository তৈরি করার জন্য নিজের GitHub account থাকতে হবে।
- আলাদাভাবে .NET SDK, Node.js বা PostgreSQL ইনস্টল করার প্রয়োজন নেই; Docker container-এর মধ্যে এগুলো ব্যবহৃত হবে।

## ২. Environment file তৈরি

VS Code Terminal-এ নিশ্চিত হও যে তুমি Project root folder-এ আছ—যে folder-এ `docker-compose.yml` আছে। তারপর PowerShell-এ চালাও:

```powershell
Copy-Item .env.example .env
```

`.env` ফাইলে `POSTGRES_PASSWORD` নিজের মতো শক্তিশালী password দিয়ে পরিবর্তন করো। `.env` GitHub-এ upload করবে না।

## ৩. Application চালু

Docker Desktop চালু রেখে Project root-এ চালাও:

```powershell
docker compose up --build -d
```

Container status দেখো:

```powershell
docker compose ps
```

সব ঠিক থাকলে PostgreSQL-এর status `healthy` এবং Backend/Frontend-এর status `Up` থাকবে।

Browser-এ খোলো:

- Frontend: <http://localhost:5173>
- Backend health: <http://localhost:8080/health>

## ৪. UI দিয়ে মূল workflow পরীক্ষা

1. উপরের ঘরে শিক্ষার্থীর ১৭ সংখ্যার জন্মনিবন্ধন নম্বর দাও।
2. `School Admin` tab থেকে `Class 6 - Morning Shift`-এর মতো একটি Class তৈরি করো এবং Seat সংখ্যা নির্ধারণ করো।
3. `ভর্তির Seat` tab-এ ফিরে Total, Held, Confirmed এবং Available Seat দেখো।
4. `আবেদন করুন` চাপলে শিক্ষার্থীর জন্য সাময়িক Seat Hold হবে।
5. `আমার আবেদন` tab-এ Hold status ও countdown দেখো।
6. `ভর্তি ফি দিন` চাপলে বাস্তব payment gateway ছাড়াই admission confirm হবে—এটি assignment-এর simulated payment requirement।
7. আরেকটি ১৭ সংখ্যার Student ID দিয়ে একই Class-এ আবেদন করে আলাদা শিক্ষার্থীর workflow পরীক্ষা করো।
8. Hold মেয়াদ শেষ হওয়ার আগে `বাতিল করুন` চাপলে Seat আবার খালি হবে।

একই Student ID ও একই Class-এর জন্য দ্বিতীয় active application তৈরি হওয়া উচিত নয়। Class-এর Seat পূর্ণ হলে নতুন application `409 Conflict` পাবে।

## ৫. Backend automated tests

এই কমান্ডটি চালাও:

```powershell
docker compose --profile test run --build --rm backend-tests
```

Backend test service আলাদা `school_admission_test` database ব্যবহার করে; মূল application database-এর data পরিবর্তন করে না। Test সফল হলে command-এর exit code `0` হবে এবং সব test pass দেখাবে।

## ৬. Frontend automated tests

```powershell
docker compose --profile test run --build --rm frontend-tests
```

Test সফল হলে Vitest-এর output-এ সব test pass দেখাবে এবং command-এর exit code `0` হবে।

## ৭. Bonus: একই Database-এ দুটি Backend Process

দ্বিতীয় Backend process চালু করো:

```powershell
docker compose --profile bonus up --build -d
```

দুটি API address:

- প্রথম process: <http://localhost:8080>
- দ্বিতীয় process: <http://localhost:8081>

দুটি process একই PostgreSQL database ব্যবহার করে। এরপর Windows PowerShell-এ ২৫টি concurrent request-এর bonus test চালাও:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\bonus-concurrency.ps1
```

সফল হলে শেষ লাইনে এমন message দেখাবে:

```text
PASS: both backend processes respected the shared PostgreSQL seat limit.
```

এই test-এ পাঁচটি Seat-এর জন্য ২৫টি concurrent request পাঠানো হয়। পাঁচটির বেশি successful hold হওয়া চলবে না।

## ৮. Database persistence পরীক্ষা

Application বন্ধ করো:

```powershell
docker compose down
```

আবার চালু করো:

```powershell
docker compose up -d
```

Browser refresh করে আগের Class ও application data আছে কি না দেখো। Named PostgreSQL volume-এর কারণে `docker compose down`-এর পরেও data থাকার কথা। **`docker compose down -v` চালাবে না**, কারণ এতে database volume মুছে যাবে।

## ৯. GitHub Repository তৈরি ও Commit

1. নিজের GitHub account-এ একটি **Public Repository** তৈরি করো।
2. Project root folder-এ PowerShell থেকে Git initialize করো:

   ```powershell
   git init
   git config user.name "Your Full Name"
   git config user.email "Your GitHub Email"
   ```

3. একটিমাত্র বড় commit না করে আলাদা meaningful commit করো। উদাহরণ:

   ```powershell
   git add .gitignore .env.example docker-compose.yml
   git commit -m "chore: add Docker Compose and environment configuration"

   git add backend
   git commit -m "feat: implement admission API and database persistence"

   git add frontend
   git commit -m "feat: implement school admission user interface"

   git add README.md GUIDE_BN.md ASSUMPTIONS.md AI_USAGE.md scripts
   git commit -m "test: add automated tests and bonus concurrency verification"
   ```

4. নিজের GitHub repository URL `origin` হিসেবে যোগ করো এবং push করো:

   ```powershell
   git branch -M main
   git remote add origin https://github.com/YOUR_USERNAME/YOUR_REPOSITORY.git
   git push -u origin main
   ```

5. GitHub-এ গিয়ে নিশ্চিত হও যে Public Repository-তে `backend/`, `frontend/`, Docker files, tests এবং documentation আছে।
6. Assignment PDF-এর confidential কপি GitHub-এ upload করবে না।
7. Assignment PDF-এর শেষ পৃষ্ঠায় দেওয়া submission link দিয়ে Public GitHub Repository URL জমা দেবে।

## ১০. জমা দেওয়ার আগে শেষ যাচাই

- [ ] `docker compose up --build -d` সফল হয়েছে।
- [ ] Frontend ও Backend খুলছে; `/health` response দিচ্ছে।
- [ ] Admin UI থেকে Class তৈরি করা যাচ্ছে।
- [ ] Student ID দিয়ে Hold তৈরি, confirm ও cancel করা যাচ্ছে।
- [ ] Hold expiry হলে Seat মুক্ত হচ্ছে এবং UI-তে payment button বন্ধ হচ্ছে।
- [ ] Backend automated tests সফল হয়েছে।
- [ ] Frontend automated tests সফল হয়েছে।
- [ ] Bonus দুই-Backend concurrency script `PASS` দেখিয়েছে।
- [ ] `docker compose down` ও পুনরায় `up` করার পর application data আছে।
- [ ] Public GitHub repository-তে একাধিক meaningful commit আছে।
- [ ] `.env` এবং confidential assignment PDF repository-তে নেই।
- [ ] `ASSUMPTIONS.md` ও `AI_USAGE.md` implementation এবং বাস্তব verification result-এর সঙ্গে মিলে।

**Deadline:** 11 November 2026, 11:59:59 PM।
